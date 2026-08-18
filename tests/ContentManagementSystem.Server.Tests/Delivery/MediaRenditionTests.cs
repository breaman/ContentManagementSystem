using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// The signed rendition endpoint under real load (tasks P5-30 and P5-13).
/// </summary>
/// <remarks>
/// The property being proved here cannot be proved by a unit test of the lock: what matters is that
/// twenty requests arriving at once, through the whole pipeline — endpoint, signature validation,
/// service, store, encoder — produce one encode. A test of <c>RenditionKeyLocks</c> in isolation
/// would pass just as happily if the delivery endpoint resolved a fresh lock registry per request,
/// which is the mistake that turns the semaphore into decoration (ADR 0007).
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class MediaRenditionTests(SqlServerFixture fixture)
{
    /// <summary>How many requests arrive at once. The number acceptance criterion P5 #9 names.</summary>
    private const int ConcurrentRequests = 20;

    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <remarks>Acceptance criterion P5 #9.</remarks>
    [Test]
    public async Task TwentyConcurrentColdRequestsForOneRenditionProduceExactlyOneEncode()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var item = await UploadAsync(cancellationToken);

        var url = SignedUrl(item, width: 640, height: 480);

        using var recorder = new RenditionRecorder(_factory.Services.GetRequiredService<IMeterFactory>());

        // One client, twenty requests started before any of them is awaited. The first to reach the
        // service takes the per-key lock and encodes; the other nineteen wait on it and then serve
        // what it persisted.
        using var client = BrowserLikeClient();

        var responses = await Task.WhenAll(Enumerable
            .Range(0, ConcurrentRequests)
            .Select(_ => client.GetAsync(url, cancellationToken)));

        try
        {
            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

                bytes.Should().NotBeEmpty("every one of the twenty gets the picture, not just the winner");
            }
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        recorder.Encodes.Should().Be(1, "nineteen of the twenty waited and then served what the first produced");

        // The row is the other half of the same claim: a second encode would either collide on the
        // unique (MediaItemId, SpecHash) index or leave a second row behind it.
        await using var scope = _factory.Services.CreateAsyncScope();

        var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .MediaRenditions
            .CountAsync(rendition => rendition.MediaItemId == item.Id, cancellationToken);

        stored.Should().Be(1);
    }

    [Test]
    public async Task ARenditionUrlWithATamperedWidthIsRefusedWithoutEncodingAnything()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var item = await UploadAsync(cancellationToken);

        // Signed for 640 and asked for at 1280. The signature covers every parameter that changes
        // the output bytes, so a client editing the path invalidates it (spec section 13.5).
        var tampered = SignedUrl(item, width: 640, height: 480).Replace("640x480", "1280x960", StringComparison.Ordinal);

        using var recorder = new RenditionRecorder(_factory.Services.GetRequiredService<IMeterFactory>());
        using var client = BrowserLikeClient();
        using var response = await client.GetAsync(tampered, cancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Forbidden);

        // The point of validating before generating: an unsigned request must cost no CPU at all,
        // or the signature has moved the denial of service rather than prevented it.
        recorder.Encodes.Should().Be(0);
    }

    /// <remarks>
    /// Acceptance criterion P5 #13, from the delivery side. The rendition URL for an item is a
    /// different string after a library edit, which is what busts browser and CDN caches with no
    /// purge to run — and the URL signed before the edit no longer validates.
    /// </remarks>
    [Test]
    public async Task ALibraryEditChangesTheRenditionUrlAndRetiresTheOldOne()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var item = await UploadAsync(cancellationToken);

        var before = SignedUrl(item, width: 640, height: 480);

        using var editor = await AdministratorAsync(_factory, cancellationToken);

        var edited = await editor.PutAsJsonAsync(
            $"{CmsApiEndpoints.BasePath}/media/{item.Id}/edits",
            new SetMediaEditsRequest(new MediaEdits(Rotate: 90)),
            cancellationToken);

        edited.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterEdit = (await edited.Content.ReadFromJsonAsync<MediaDetail>(cancellationToken))!;

        var after = SignedUrl(afterEdit, width: 640, height: 480);

        after.Should().NotBe(before);

        using var client = BrowserLikeClient();

        using var stale = await client.GetAsync(before, cancellationToken);
        using var fresh = await client.GetAsync(after, cancellationToken);

        // 410, not 200. The old URL is still signed by this site — it has to be refused on the
        // strength of the version it names, or it would serve the newly rotated picture under a key
        // that says immutable and caches say so for a year (ADR 0007).
        stale.StatusCode.Should().Be(HttpStatusCode.Gone);

        fresh.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// A client that says what a browser says.
    /// </summary>
    /// <remarks>
    /// <c>Accept</c> matters here: the endpoint negotiates WebP against it and serves the original
    /// format to a client that did not ask for WebP. A bare <see cref="HttpClient"/> sends no
    /// <c>Accept</c> at all, which is neither what a browser does nor what the negotiation is for.
    /// </remarks>
    private HttpClient BrowserLikeClient()
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Add("Accept", "image/avif,image/webp,image/*,*/*;q=0.8");

        return client;
    }

    /// <summary>Builds the signed URL the site would emit for a rendition of an item.</summary>
    private string SignedUrl(MediaDetail item, int width, int height) =>
        _factory.Services.GetRequiredService<IMediaUrlSigner>().BuildUrl(
            new RenditionSpec(
                item.Id,
                width,
                height,
                RenditionMode.Crop,
                ImageOutputFormat.Webp,
                RenditionSpec.DefaultQuality,
                item.EditsVersion),
            item.OriginalFileName);

    /// <summary>Uploads a source image through the media API and returns the stored item.</summary>
    private async Task<MediaDetail> UploadAsync(CancellationToken cancellationToken)
    {
        using var client = await ClientAsync(_factory, cancellationToken, CmsRoles.Administrator);

        using var bitmap = new SKBitmap(2000, 1500);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SeaGreen);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        using var body = new MultipartFormDataContent
        {
            { new ByteArrayContent(data.ToArray()), "file", $"source-{Guid.NewGuid():N}.jpg" },
            { new StringContent("A source photograph"), "altText" },
        };

        var response = await client.PostAsync($"{CmsApiEndpoints.BasePath}/media", body, cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!.Item;
    }

    /// <summary>
    /// Counts the encodes the meter reports, which is the only place "did this cost CPU?" is
    /// answered.
    /// </summary>
    /// <remarks>
    /// The counter rather than a wrapped <c>IImageProcessor</c>, deliberately: the instrument is the
    /// one an operator will watch in production, so a test that counted something else could pass
    /// while the dashboard stayed at zero (task P5-32, spec section 24.1).
    /// </remarks>
    private sealed class RenditionRecorder : IDisposable
    {
        private readonly MeterListener _listener;
        private long _encodes;

        public RenditionRecorder(IMeterFactory factory)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name != CmsTelemetry.MeterName) return;
                    if (!ReferenceEquals(instrument.Meter.Scope, factory)) return;
                    if (instrument.Name != CmsMetrics.RenditionGeneratedName) return;

                    listener.EnableMeasurementEvents(instrument);
                },
            };

            _listener.SetMeasurementEventCallback<long>(
                (_, measurement, _, _) => Interlocked.Add(ref _encodes, measurement));

            _listener.Start();
        }

        /// <summary>How many renditions were actually encoded since this recorder started.</summary>
        public long Encodes => Interlocked.Read(ref _encodes);

        public void Dispose() => _listener.Dispose();
    }
}
