using System.Diagnostics.Metrics;

namespace ContentManagementSystem.Core.Telemetry;

/// <summary>
/// The CMS's instruments (task P2-29, spec section 24.1).
/// </summary>
/// <remarks>
/// Built from <see cref="IMeterFactory"/> rather than by constructing a <see cref="Meter"/> directly.
/// The factory scopes the meter to the container, which is what lets a test host observe its own
/// instruments without seeing another host's — two <c>WebApplicationFactory</c> instances in one test
/// process otherwise publish to the same process-wide meter, and a measurement assertion becomes a
/// race against whatever else is running.
/// <para>
/// Registered as a singleton. Instruments are cheap to record on and expensive to create, and an
/// instrument created per request produces a fresh time series each time.
/// </para>
/// </remarks>
public sealed class CmsMetrics
{
    /// <summary>Name of the counter of publish attempts.</summary>
    public const string PublishCountName = "cms.publish.count";

    /// <summary>Name of the histogram of publish durations.</summary>
    public const string PublishDurationName = "cms.publish.duration";

    /// <summary>Tag naming how the attempt ended (spec section 24.1).</summary>
    public const string ResultTag = "result";

    private readonly Counter<long> _publishCount;
    private readonly Histogram<double> _publishDuration;

    /// <summary>Creates the instruments on the CMS meter.</summary>
    /// <param name="meterFactory">Factory supplying the container-scoped meter.</param>
    public CmsMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(CmsTelemetry.MeterName);

        _publishCount = meter.CreateCounter<long>(
            PublishCountName,
            unit: "{publish}",
            description: "Publish attempts, tagged by how they ended.");

        _publishDuration = meter.CreateHistogram<double>(
            PublishDurationName,
            unit: "ms",
            description: "Wall-clock time one publish attempt took, tagged by how it ended.");
    }

    /// <summary>
    /// Records one publish attempt.
    /// </summary>
    /// <param name="result">One of <see cref="CmsTelemetry.PublishResults"/>.</param>
    /// <param name="elapsed">How long the attempt took.</param>
    /// <remarks>
    /// Both instruments are recorded together and carry the same tag, so the count of a result and
    /// the duration of that result can never disagree about how many attempts there were. Every
    /// attempt is recorded, including the refused and the failed ones — a duration histogram of only
    /// the successes hides the case where publishing became slow enough to start timing out.
    /// </remarks>
    public void RecordPublish(string result, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>(ResultTag, result);

        _publishCount.Add(1, tag);
        _publishDuration.Record(elapsed.TotalMilliseconds, tag);
    }
}
