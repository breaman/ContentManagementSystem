using System.Diagnostics;
using System.Diagnostics.Metrics;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// The publish metrics and span of spec section 24.1 (task P2-29).
/// </summary>
/// <remarks>
/// Asserted through a real publish rather than by calling <c>CmsMetrics</c> directly. What can
/// actually go wrong is not the counter — it is the counter never being reached: an early return
/// that skips the recording, a <c>finally</c> that was never written, a name that drifted from the
/// one the dashboard queries. Every one of those passes a test that records a measurement by hand.
/// <para>
/// Measurements are filtered by the meter's <see cref="Meter.Scope"/>, which is the service provider
/// that created it. Two hosts in one test process otherwise publish to instruments of the same name,
/// and a count assertion becomes a race against whatever else the suite is doing.
/// </para>
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class PublishTelemetryTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private FailingSaveInterceptor _interceptor = null!;
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync()
    {
        _interceptor = new FailingSaveInterceptor();
        _bench = await PageWorkbench.CreateAsync(
            fixture,
            cancellationToken: TestContext.Current.CancellationToken,
            interceptor: _interceptor);
    }

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task ASuccessfulPublishCountsOnceAndRecordsHowLongItTook()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recorder = Recorder(_bench);
        var page = await ReadyToPublishAsync("telemetry-ok", cancellationToken);

        var result = await _bench.Resolve<IPublishingService>().PublishAsync(
            page,
            cancellationToken: cancellationToken);

        result.IsSuccess.Should().BeTrue();

        recorder.Counts(CmsTelemetry.PublishResults.Published).Should().Be(1);

        // The histogram is recorded with the same tag as the counter, on every attempt. A duration
        // series that only covers the successes hides publishing becoming slow enough to time out.
        recorder.Durations(CmsTelemetry.PublishResults.Published).Should().ContainSingle()
            .Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ARefusedPublishIsCountedAsRefusedAndNotAsAFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recorder = Recorder(_bench);
        var page = await ReadyToPublishAsync("telemetry-refused", cancellationToken, fill: false);

        var result = await _bench.Resolve<IPublishingService>().PublishAsync(
            page,
            cancellationToken: cancellationToken);

        result.IsSuccess.Should().BeFalse();

        // An editor being told their required zone is empty is not an incident. Tagging it as one
        // would bury the case that is — a publish path that threw — under ordinary editing noise.
        recorder.Counts(CmsTelemetry.PublishResults.Refused).Should().Be(1);
        recorder.Counts(CmsTelemetry.PublishResults.Failed).Should().Be(0);
        recorder.Counts(CmsTelemetry.PublishResults.Published).Should().Be(0);
    }

    [Fact]
    public async Task APublishAgainstNoSuchPageIsCountedAsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var recorder = Recorder(_bench);

        var result = await _bench.Resolve<IPublishingService>().PublishAsync(
            999_999,
            cancellationToken: cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.NotFound);
        recorder.Counts(CmsTelemetry.PublishResults.NotFound).Should().Be(1);
    }

    [Fact]
    public async Task APublishTheCallerMayNotMakeIsCountedAsForbidden()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // Its own host, because a workbench's permissions are fixed when it is built — and this is
        // the one outcome that is decided before the service touches the database at all.
        await using var reader = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead, CmsPermissions.ContentEdit),
            cancellationToken);

        using var recorder = Recorder(reader);

        var result = await reader.Resolve<IPublishingService>().PublishAsync(1, cancellationToken: cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.Forbidden);
        recorder.Counts(CmsTelemetry.PublishResults.Forbidden).Should().Be(1);
    }

    [Fact]
    public async Task APublishThatThrowsIsStillCountedAndIsCountedAsFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await ReadyToPublishAsync("telemetry-failed", cancellationToken);

        using var recorder = Recorder(_bench);

        _interceptor.FailOnCall(1);

        var attempt = async () => await _bench.Resolve<IPublishingService>().PublishAsync(
            page,
            cancellationToken: cancellationToken);

        await attempt.Should().ThrowAsync<InvalidOperationException>();

        _interceptor.Reset();

        // The measurement is taken in a finally. An operation that vanishes from its own counter
        // when it breaks is worse than no counter at all: the graph stays flat and healthy while
        // publishing is down, which is risk R4 arriving invisibly.
        recorder.Counts(CmsTelemetry.PublishResults.Failed).Should().Be(1);
        recorder.Durations(CmsTelemetry.PublishResults.Failed).Should().ContainSingle();
    }

    [Fact]
    public async Task EveryPublishStartsASpanCarryingThePageAndTheOutcome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var spans = new List<Activity>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == CmsTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = spans.Add,
        };

        ActivitySource.AddActivityListener(listener);

        var page = await ReadyToPublishAsync("telemetry-span", cancellationToken);
        var publishing = _bench.Resolve<IPublishingService>();

        (await publishing.PublishAsync(page, cancellationToken: cancellationToken))
            .IsSuccess.Should().BeTrue();

        var refused = await ReadyToPublishAsync("telemetry-span-refused", cancellationToken, fill: false);

        (await publishing.PublishAsync(refused, cancellationToken: cancellationToken))
            .IsSuccess.Should().BeFalse();

        var published = spans.Single(span => Tag(span, CmsTelemetry.PageIdTag) == page.ToString());

        published.OperationName.Should().Be(CmsTelemetry.PublishActivityName);
        published.Status.Should().Be(ActivityStatusCode.Unset);
        Tag(published, CmsMetrics.ResultTag).Should().Be(CmsTelemetry.PublishResults.Published);
        Tag(published, CmsTelemetry.VersionNumberTag).Should().Be("2");

        // A refusal is an ordinary outcome for the editor and an error for the trace: a span is read
        // to find out why a request did not do what was asked of it.
        var stopped = spans.Single(span => Tag(span, CmsTelemetry.PageIdTag) == refused.ToString());

        stopped.Status.Should().Be(ActivityStatusCode.Error);
        Tag(stopped, CmsMetrics.ResultTag).Should().Be(CmsTelemetry.PublishResults.Refused);
    }

    /// <summary>
    /// Reads one tag off a span.
    /// </summary>
    /// <remarks>
    /// Through <see cref="Activity.GetTagItem"/> rather than <c>Tags</c>, which enumerates only the
    /// tags whose value is a string — the page id and the version number are integers, and are
    /// invisible there.
    /// </remarks>
    private static string? Tag(Activity activity, string name) =>
        activity.GetTagItem(name)?.ToString();

    /// <summary>Creates a page on its own template and optionally fills its required zone.</summary>
    /// <param name="templateKey">Key for the template, unique within this class's database.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="fill">Whether to fill the required zone, which is what makes a publish succeed.</param>
    /// <returns>Identity of the page.</returns>
    private async Task<int> ReadyToPublishAsync(
        string templateKey,
        CancellationToken cancellationToken,
        bool fill = true)
    {
        var template = await _bench.AddTemplateAsync(
            templateKey,
            cancellationToken,
            PageWorkbench.TextZone("hero", required: true));

        // Titled after the template, since a slug is unique among its siblings and one test
        // here creates two pages at the root.
        var page = await _bench.AddPageAsync(template, templateKey, cancellationToken);

        if (fill)
        {
            var saved = await _bench.Resolve<IDraftService>().SaveAsync(
                page.Summary.Id,
                new SaveDraftRequest(
                    $$"""
                    { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                      "zones": { "hero": { "type": "plainText", "value": "Live" } } }
                    """,
                    null),
                cancellationToken);

            saved.IsSuccess.Should().BeTrue();
        }

        return page.Summary.Id;
    }

    /// <summary>Listens to one host's CMS instruments for as long as it is alive.</summary>
    private static MeasurementRecorder Recorder(PageWorkbench bench) =>
        new(bench.Resolve<IMeterFactory>());

    /// <summary>
    /// Collects the measurements one host published on the CMS meter.
    /// </summary>
    /// <remarks>
    /// Scoped to the meter factory that made the instrument, so a host started by another suite in
    /// the same process cannot contribute to a count asserted here.
    /// </remarks>
    private sealed class MeasurementRecorder : IDisposable
    {
        private readonly List<(string Instrument, double Value, string? Result)> _measurements = [];
        private readonly MeterListener _listener;

        public MeasurementRecorder(IMeterFactory factory)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name != CmsTelemetry.MeterName) return;
                    if (!ReferenceEquals(instrument.Meter.Scope, factory)) return;

                    listener.EnableMeasurementEvents(instrument);
                },
            };

            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags));

            _listener.Start();
        }

        /// <summary>How many publish attempts were counted with the given result tag.</summary>
        public long Counts(string result) =>
            _measurements
                .Where(entry => entry.Instrument == CmsMetrics.PublishCountName && entry.Result == result)
                .Sum(entry => (long)entry.Value);

        /// <summary>The durations recorded with the given result tag.</summary>
        public IReadOnlyList<double> Durations(string result) =>
            [.. _measurements
                .Where(entry => entry.Instrument == CmsMetrics.PublishDurationName && entry.Result == result)
                .Select(entry => entry.Value)];

        public void Dispose() => _listener.Dispose();

        private void Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? result = null;

            foreach (var tag in tags)
            {
                if (tag.Key == CmsMetrics.ResultTag) result = tag.Value?.ToString();
            }

            lock (_measurements)
            {
                _measurements.Add((instrument, value, result));
            }
        }
    }
}
