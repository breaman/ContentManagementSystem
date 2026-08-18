using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using SkiaSharp;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// NFR-8: a cold 4000 px source renders to a 1280 px WebP in under 800 ms at the 95th percentile
/// (task P5-32, spec sections 24.1 and 28).
/// </summary>
/// <remarks>
/// <strong>Measured through the endpoint, and only the cold path.</strong> A benchmark of
/// <c>IImageProcessor</c> alone would report the encode and miss everything the requirement is
/// actually about — reading the original out of the store, taking the per-key lock, writing the
/// rendition back, and recording the row. Those are what turn a fast encoder into a slow first
/// request, and they are exactly what a caching layer hides on the second one.
/// <para>
/// Each sample asks for a rendition nothing has produced, by varying the crop by a fraction of a
/// pixel. That keeps every request genuinely cold without needing a fresh item per sample: the crop
/// is part of the spec, so it is part of the hash, so nothing already in storage matches it.
/// </para>
/// <para>
/// The assertion is on the <em>telemetry</em> as well as on the wall clock, because the second half
/// of the task is that an operator can watch this number in production. A benchmark that passed
/// while <c>cms.media.rendition.duration</c> recorded nothing would leave the requirement
/// unobservable the day it starts being missed.
/// </para>
/// <para>
/// <strong>The threshold is a budget, not a stopwatch precision claim.</strong> Timings on a shared
/// build agent are noisy, so the percentile is taken over enough samples to be meaningful and the
/// slowest sample is reported when it fails — a bare "false" on a performance assertion is the
/// hardest kind of failure to act on.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class RenditionBenchmarkTests(SqlServerFixture fixture)
{
    /// <summary>Width of the source, as NFR-8 names it.</summary>
    private const int SourceWidth = 4000;

    /// <summary>Width of the rendition, as NFR-8 names it.</summary>
    private const int RenditionWidth = 1280;

    /// <summary>The budget, in milliseconds.</summary>
    private const double BudgetMilliseconds = 800;

    /// <summary>How many cold renditions are measured.</summary>
    /// <remarks>
    /// Twenty, so the 95th percentile is the second-slowest sample rather than a synonym for the
    /// worst one. Fewer would make "p95" a rounding decision; many more would make a benchmark that
    /// runs on every build cost more than it is worth.
    /// </remarks>
    private const int Samples = 20;

    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task AColdFourThousandPixelSourceRendersToTwelveEightyWebpWellInsideTheBudget()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var item = await UploadSourceAsync(cancellationToken);

        item.Width.Should().Be(SourceWidth, "NFR-8 is stated against a 4000 px source");

        using var recorder = new RenditionMeasurements(_factory.Services.GetRequiredService<IMeterFactory>());

        var signer = _factory.Services.GetRequiredService<IMediaUrlSigner>();

        using var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Add("Accept", "image/avif,image/webp,image/*,*/*;q=0.8");

        var elapsed = new List<double>(Samples);

        // One warm request first, and its timing is discarded. What it pays for is everything that
        // is cold once per process rather than once per rendition — the native decoder loading, the
        // first database connection, the JIT — none of which NFR-8 is about.
        await MeasureAsync(client, signer, item, sample: -1, cancellationToken);

        for (var sample = 0; sample < Samples; sample++)
        {
            elapsed.Add(await MeasureAsync(client, signer, item, sample, cancellationToken));
        }

        elapsed.Sort();

        var p95 = elapsed[(int)Math.Ceiling(elapsed.Count * 0.95) - 1];

        p95.Should().BeLessThan(
            BudgetMilliseconds,
            "NFR-8 allows {0} ms; the slowest of {1} cold renditions took {2:F0} ms",
            BudgetMilliseconds,
            Samples,
            elapsed[^1]);

        // The other half of the task: the numbers an operator watches have to move (spec section
        // 24.1). A benchmark that only timed a stopwatch could pass with the dashboard flat.
        recorder.Encodes.Should().Be(
            Samples + 1,
            "every sample asked for a rendition nothing had produced, so every one paid for an encode");

        recorder.Durations.Should().HaveCount(Samples + 1);
        recorder.Formats.Should().OnlyContain(format => format == "webp");
    }

    /// <summary>Times one cold rendition request end to end.</summary>
    /// <param name="client">A browser-like client, so WebP is negotiated rather than the original.</param>
    /// <param name="signer">Signs the URL, as the rendering path would.</param>
    /// <param name="item">The source item.</param>
    /// <param name="sample">
    /// The sample number, which becomes a fractional crop offset so that no two requests name the
    /// same rendition.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Wall-clock milliseconds, including the response body.</returns>
    private static async Task<double> MeasureAsync(
        HttpClient client,
        IMediaUrlSigner signer,
        MediaDetail item,
        int sample,
        CancellationToken cancellationToken)
    {
        // A crop that differs in the fourth decimal — which is the precision the spec is canonicalized
        // to, so each of these really is a distinct rendition rather than a rounding collision.
        var crop = new NormalizedRect(0, 0, 1 - ((sample + 2) * 0.0001), 1);

        var url = signer.BuildUrl(
            new RenditionSpec(
                item.Id,
                RenditionWidth,
                RenditionWidth * 3 / 4,
                RenditionMode.Crop,
                ImageOutputFormat.Webp,
                RenditionSpec.DefaultQuality,
                item.EditsVersion,
                Crop: crop),
            item.OriginalFileName);

        var started = Stopwatch.GetTimestamp();

        using var response = await client.GetAsync(url, cancellationToken);

        // The body is read inside the measurement: a response whose headers arrived quickly and
        // whose bytes did not is not a fast rendition, and streaming would let one look like one.
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        bytes.Should().NotBeEmpty();

        return elapsed;
    }

    /// <summary>Uploads a 4000 px photograph-like source through the real pipeline.</summary>
    /// <remarks>
    /// Noise rather than a flat fill. A single-colour image compresses to almost nothing and encodes
    /// far faster than a photograph, so benchmarking against one would report a number no real
    /// upload could reproduce — the direction that makes a performance test quietly worthless.
    /// </remarks>
    private async Task<MediaDetail> UploadSourceAsync(CancellationToken cancellationToken)
    {
        using var client = await AdministratorAsync(_factory, cancellationToken);

        using var bitmap = new SKBitmap(SourceWidth, SourceWidth * 3 / 4);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SlateGray);

            var random = new Random(Seed: 20260816);

            using var paint = new SKPaint();

            for (var i = 0; i < 60_000; i++)
            {
                paint.Color = new SKColor(
                    (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));

                canvas.DrawRect(random.Next(bitmap.Width), random.Next(bitmap.Height), 16, 16, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);

        using var body = new MultipartFormDataContent
        {
            { new ByteArrayContent(data.ToArray()), "file", $"benchmark-{Guid.NewGuid():N}.jpg" },
            { new StringContent("A benchmark photograph"), "altText" },
        };

        var response = await client.PostAsync($"{CmsApiEndpoints.BasePath}/media", body, cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.Created,
            await response.Content.ReadAsStringAsync(cancellationToken));

        return (await response.Content.ReadFromJsonAsync<MediaUploadResult>(cancellationToken))!.Item;
    }

    /// <summary>
    /// Listens to both rendition instruments, so the benchmark asserts on what an operator sees.
    /// </summary>
    /// <remarks>
    /// The counter and the histogram are recorded together by <c>CmsMetrics</c>, and this reads them
    /// separately on purpose: if the two ever disagreed about how many encodes happened, the
    /// dashboard would show a rate and a latency that were about different populations.
    /// </remarks>
    private sealed class RenditionMeasurements : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly List<double> _durations = [];
        private readonly List<string> _formats = [];
        private readonly Lock _gate = new();

        private long _encodes;

        public RenditionMeasurements(IMeterFactory factory)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name != CmsTelemetry.MeterName) return;
                    if (!ReferenceEquals(instrument.Meter.Scope, factory)) return;

                    if (instrument.Name is CmsMetrics.RenditionGeneratedName
                        or CmsMetrics.RenditionDurationName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };

            _listener.SetMeasurementEventCallback<long>(
                (_, measurement, _, _) => Interlocked.Add(ref _encodes, measurement));

            _listener.SetMeasurementEventCallback<double>((_, measurement, tags, _) =>
            {
                lock (_gate)
                {
                    _durations.Add(measurement);

                    foreach (var tag in tags)
                    {
                        if (tag.Key == CmsMetrics.FormatTag) _formats.Add(tag.Value?.ToString() ?? string.Empty);
                    }
                }
            });

            _listener.Start();
        }

        /// <summary>How many renditions were actually encoded since this recorder started.</summary>
        public long Encodes => Interlocked.Read(ref _encodes);

        /// <summary>Every duration the histogram recorded, in milliseconds.</summary>
        public IReadOnlyList<double> Durations
        {
            get
            {
                lock (_gate)
                {
                    return [.. _durations];
                }
            }
        }

        /// <summary>The format tag each measurement carried.</summary>
        public IReadOnlyList<string> Formats
        {
            get
            {
                lock (_gate)
                {
                    return [.. _formats];
                }
            }
        }

        public void Dispose() => _listener.Dispose();
    }
}
