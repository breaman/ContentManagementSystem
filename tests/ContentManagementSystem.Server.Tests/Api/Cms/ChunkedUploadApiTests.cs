using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using ContentManagementSystem.Core.Media.Upload;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The resumable upload endpoints over real HTTP (task P5-08, spec section 13.3).
/// </summary>
/// <remarks>
/// Two claims are worth proving here and neither is provable at the service alone. The first is that
/// a file which arrives in parts ends up as exactly the same item a single request would have
/// produced — same bytes, same hash, same deduplication — because the parts are assembled and then
/// handed to the one pipeline. The second is that the resumable route is not a way around any of
/// that pipeline's refusals, which is the failure mode a chunked uploader invites: a back door that
/// looks like a feature.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class ChunkedUploadApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private const string Uploads = $"{CmsApiEndpoints.BasePath}/media/uploads";

    /// <summary>Part size these tests run at, small enough that a modest fixture is several parts.</summary>
    private const int ChunkBytes = 64 * 1024;

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync()
    {
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

        // Configured down from the deployment default so a test fixture does not have to be four
        // megabytes to be more than one part. The service clamps a configured size upwards when it
        // would produce too many parts, never downwards, so this is honoured exactly.
        _factory.Services.GetRequiredService<MediaUploadOptions>().ChunkBytes = ChunkBytes;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AFileSentInPartsBecomesTheSameItemASingleRequestWouldHaveProduced()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var bytes = Jpeg(1600, 1200, seed: 11);

        bytes.Length.Should().BeGreaterThan(ChunkBytes, "the fixture has to be more than one part");

        var session = await StartAsync(client, "holiday.jpg", bytes.Length, cancellationToken);

        session.ChunkSize.Should().Be(ChunkBytes);
        session.ReceivedBytes.Should().Be(0);
        session.NextChunkIndex.Should().Be(0);

        session = await SendPartsAsync(client, session, bytes, cancellationToken);

        session.IsComplete.Should().BeTrue();
        session.ReceivedBytes.Should().Be(bytes.Length);

        var completed = await client.PostAsync(
            $"{Uploads}/{session.UploadId}/complete",
            content: null,
            cancellationToken);

        completed.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await completed.Content.ReadAsStringAsync(cancellationToken));

        var upload = (await completed.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!;

        upload.Deduplicated.Should().BeFalse();
        upload.Item.Kind.Should().Be("Image");
        upload.Item.Width.Should().Be(1600);
        upload.Item.Height.Should().Be(1200);

        // The same file sent the ordinary way now deduplicates onto it. That is the strongest
        // statement available that the parts were reassembled in the right order and unaltered: the
        // hash is taken over the normalized bytes, and one byte out of place would produce a second
        // item rather than a match (spec section 13.1).
        using var single = new MultipartFormDataContent
        {
            { new ByteArrayContent(bytes), "file", "holiday-again.jpg" },
            { new StringContent("A test photograph"), "altText" },
        };

        var again = await client.PostAsync($"{CmsApiEndpoints.BasePath}/media", single, cancellationToken);

        again.StatusCode.Should().Be(HttpStatusCode.OK);

        var duplicate = (await again.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!;

        duplicate.Deduplicated.Should().BeTrue();
        duplicate.Item.Id.Should().Be(upload.Item.Id);
    }

    /// <remarks>
    /// The property that makes the upload resumable rather than merely restartable: the server says
    /// where it got to, and a client that lost its connection continues from there.
    /// </remarks>
    [Fact]
    public async Task AnInterruptedUploadReportsWhereItGotToAndContinuesFromThere()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var bytes = Jpeg(1600, 1200, seed: 23);
        var session = await StartAsync(client, "interrupted.jpg", bytes.Length, cancellationToken);

        // One part, and then the client "disappears".
        session = await SendPartAsync(client, session, bytes, index: 0, cancellationToken);

        session.ReceivedBytes.Should().Be(ChunkBytes);

        var resumed = await client.GetFromJsonAsync<ChunkedUploadSession>(
            $"{Uploads}/{session.UploadId}",
            cancellationToken);

        resumed!.NextChunkIndex.Should().Be(1);
        resumed.ReceivedBytes.Should().Be(ChunkBytes);
        resumed.IsComplete.Should().BeFalse();

        // Continuing from the index the server named finishes the file.
        var finished = await SendPartsAsync(client, resumed, bytes, cancellationToken);

        finished.IsComplete.Should().BeTrue();

        var completed = await client.PostAsync(
            $"{Uploads}/{finished.UploadId}/complete",
            content: null,
            cancellationToken);

        completed.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task APartOfferedOutOfOrderIsRefusedAndTheRefusalNamesTheOneExpected()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var bytes = Jpeg(1600, 1200, seed: 31);
        var session = await StartAsync(client, "out-of-order.jpg", bytes.Length, cancellationToken);

        using var part = new ByteArrayContent(bytes, 0, ChunkBytes);

        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        // Part 4 while the server is waiting for part 0. Accepting it would leave a hole the
        // assembled file could never be checked for.
        var response = await client.PutAsync(
            $"{Uploads}/{session.UploadId}/parts/4",
            part,
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        body.Should().Contain(MediaCodes.UploadChunkOutOfOrder).And.Contain("part 0");
    }

    [Fact]
    public async Task FinishingBeforeEveryByteHasArrivedIsRefused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var bytes = Jpeg(1600, 1200, seed: 41);
        var session = await StartAsync(client, "half.jpg", bytes.Length, cancellationToken);

        session = await SendPartAsync(client, session, bytes, index: 0, cancellationToken);

        var completed = await client.PostAsync(
            $"{Uploads}/{session.UploadId}/complete",
            content: null,
            cancellationToken);

        completed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await completed.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain(MediaCodes.UploadIncomplete);
    }

    /// <remarks>
    /// The refusal that matters most. A chunked route that screened less than the single-request one
    /// would be a way into the library that skips the sniffer, and it would look like a working
    /// feature until somebody noticed an HTML file being served from the site's own origin
    /// (spec section 20.7).
    /// </remarks>
    [Fact]
    public async Task AFileWhoseBytesDisagreeWithItsNameIsRefusedWhenTheSessionIsFinished()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var html = System.Text.Encoding.UTF8.GetBytes(
            "<html><body><script>alert(document.cookie)</script></body></html>");

        var session = await StartAsync(client, "photo.jpg", html.Length, cancellationToken);

        session = await SendPartsAsync(client, session, html, cancellationToken);

        session.IsComplete.Should().BeTrue("the transport does not care what the bytes are");

        var completed = await client.PostAsync(
            $"{Uploads}/{session.UploadId}/complete",
            content: null,
            cancellationToken);

        completed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await completed.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain(MediaCodes.TypeMismatch);
    }

    [Fact]
    public async Task AnExtensionTheSiteDoesNotAcceptIsRefusedBeforeAnyBytesAreTransferred()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var response = await client.PostAsJsonAsync(
            Uploads,
            new StartChunkedUploadRequest("payload.exe", 10 * 1024 * 1024),
            cancellationToken);

        // The whole point of declaring the file up front: an editor must not watch a progress bar
        // reach the end of an upload that was never going to be accepted.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain(MediaCodes.ExtensionNotAllowed);
    }

    [Fact]
    public async Task AFileLargerThanTheLimitIsRefusedBeforeAnyBytesAreTransferred()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var response = await client.PostAsJsonAsync(
            Uploads,
            new StartChunkedUploadRequest("enormous.jpg", MediaUploadOptions.DefaultMaxImageBytes + 1),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain(MediaCodes.TooLarge);
    }

    [Fact]
    public async Task AnAbandonedSessionStopsExistingAndItsPartsGoWithIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var bytes = Jpeg(1600, 1200, seed: 53);
        var session = await StartAsync(client, "abandoned.jpg", bytes.Length, cancellationToken);

        session = await SendPartAsync(client, session, bytes, index: 0, cancellationToken);

        var abandoned = await client.DeleteAsync($"{Uploads}/{session.UploadId}", cancellationToken);

        abandoned.StatusCode.Should().Be(HttpStatusCode.OK);

        using var gone = await client.GetAsync($"{Uploads}/{session.UploadId}", cancellationToken);

        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await gone.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain(MediaCodes.UploadSessionNotFound);
    }

    /// <remarks>
    /// An upload id is the one part of a media storage key that arrives from a client, so it is
    /// checked for shape rather than sanitized. A traversal attempt therefore reads as "no such
    /// session" rather than reaching the store at all (spec section 13.2).
    /// </remarks>
    [Fact]
    public async Task AnUploadIdThatIsNotOneThisServerIssuedResolvesToNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        foreach (var candidate in (string[])["..%2f..%2fetc", "not-a-guid", new string('z', 32)])
        {
            using var response = await client.GetAsync($"{Uploads}/{candidate}", cancellationToken);

            response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task AnEditorWithoutTheUploadPermissionCannotOpenASession()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // A viewer may browse the library and may not add to it.
        using var client = await ClientAsync(_factory, cancellationToken, CmsRoles.Viewer);

        var response = await client.PostAsJsonAsync(
            Uploads,
            new StartChunkedUploadRequest("photo.jpg", 1024),
            cancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized);
    }

    /// <summary>Opens a session, failing loudly if the API refused it.</summary>
    private static async Task<ChunkedUploadSession> StartAsync(
        HttpClient client,
        string fileName,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        var response = await client.PostAsJsonAsync(
            Uploads,
            new StartChunkedUploadRequest($"{Guid.NewGuid():N}-{fileName}", totalBytes, AltText: "A test photograph"),
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<ChunkedUploadSession>(cancellationToken))!;
    }

    /// <summary>Sends every part still outstanding.</summary>
    private static async Task<ChunkedUploadSession> SendPartsAsync(
        HttpClient client,
        ChunkedUploadSession session,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        while (!session.IsComplete)
        {
            session = await SendPartAsync(client, session, bytes, session.NextChunkIndex, cancellationToken);
        }

        return session;
    }

    /// <summary>Sends one part, cut from the file at the index's own offset.</summary>
    private static async Task<ChunkedUploadSession> SendPartAsync(
        HttpClient client,
        ChunkedUploadSession session,
        byte[] bytes,
        int index,
        CancellationToken cancellationToken)
    {
        var offset = index * session.ChunkSize;
        var length = (int)Math.Min(session.ChunkSize, bytes.Length - offset);

        using var part = new ByteArrayContent(bytes, offset, length);

        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

        var response = await client.PutAsync(
            $"{Uploads}/{session.UploadId}/parts/{index}",
            part,
            cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<ChunkedUploadSession>(cancellationToken))!;
    }

    /// <summary>A JPEG whose bytes differ per seed, so two fixtures do not deduplicate onto each other.</summary>
    private static byte[] Jpeg(int width, int height, int seed)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor((byte)(seed * 7 % 256), (byte)(seed * 13 % 256), (byte)(seed * 29 % 256)));

            // Noise, so the encoder cannot compress the image down to less than one part.
            var random = new Random(seed);

            using var paint = new SKPaint();

            for (var i = 0; i < 4000; i++)
            {
                paint.Color = new SKColor(
                    (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

                canvas.DrawRect(random.Next(width), random.Next(height), 12, 12, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);

        return data.ToArray();
    }
}
