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

    /// <summary>Name of the histogram of page render durations (task P3-28).</summary>
    public const string PageRenderDurationName = "cms.page.render.duration";

    /// <summary>Name of the counter of URLs that resolved to nothing (task P3-28).</summary>
    public const string RouteResolutionMissName = "cms.route.resolution.miss";

    /// <summary>Name of the counter of image renditions encoded (task P5-32, spec section 24.1).</summary>
    public const string RenditionGeneratedName = "cms.media.rendition.generated";

    /// <summary>Name of the histogram of rendition encode durations (task P5-32).</summary>
    public const string RenditionDurationName = "cms.media.rendition.duration";

    /// <summary>Tag naming the output format a rendition was encoded in.</summary>
    public const string FormatTag = "format";

    /// <summary>Tag naming how the attempt ended (spec section 24.1).</summary>
    public const string ResultTag = "result";

    /// <summary>Tag naming the template a page was rendered with (spec section 24.1).</summary>
    public const string TemplateTag = "template";

    /// <summary>Tag saying whether the render was served from a cache (spec section 24.1).</summary>
    public const string CacheHitTag = "cache_hit";

    private readonly Counter<long> _publishCount;
    private readonly Histogram<double> _publishDuration;
    private readonly Histogram<double> _pageRenderDuration;
    private readonly Counter<long> _routeResolutionMiss;
    private readonly Counter<long> _renditionGenerated;
    private readonly Histogram<double> _renditionDuration;

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

        _pageRenderDuration = meter.CreateHistogram<double>(
            PageRenderDurationName,
            unit: "ms",
            description: "Wall-clock time one public page render took, tagged by template.");

        _routeResolutionMiss = meter.CreateCounter<long>(
            RouteResolutionMissName,
            unit: "{request}",
            description: "Requests whose URL resolved to neither a page nor a redirect.");

        _renditionGenerated = meter.CreateCounter<long>(
            RenditionGeneratedName,
            unit: "{rendition}",
            description: "Image renditions actually encoded, as opposed to served from storage.");

        _renditionDuration = meter.CreateHistogram<double>(
            RenditionDurationName,
            unit: "ms",
            description: "Wall-clock time one rendition encode took, tagged by output format.");
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

    /// <summary>
    /// Records one public page render (spec section 24.1, task P3-28).
    /// </summary>
    /// <param name="templateKey">The template the page was rendered with.</param>
    /// <param name="elapsed">How long the render took.</param>
    /// <param name="cacheHit">Whether the response came from a cache rather than being rendered.</param>
    /// <remarks>
    /// Tagged by template because that is the dimension a regression actually has: pages are not
    /// slow, <em>templates</em> are, and an untagged histogram of every page on the site averages the
    /// one expensive layout into invisibility. The page id is deliberately not a tag — one time
    /// series per page is how a metrics bill and a collector both fall over.
    /// <para>
    /// <paramref name="cacheHit"/> is recorded from the first release even though output caching does
    /// not arrive until Phase 8. A histogram whose meaning silently changes when caching is switched
    /// on is worse than one that was always able to say which of the two it measured.
    /// </para>
    /// </remarks>
    public void RecordPageRender(string templateKey, TimeSpan elapsed, bool cacheHit = false) =>
        _pageRenderDuration.Record(
            elapsed.TotalMilliseconds,
            new KeyValuePair<string, object?>(TemplateTag, templateKey),
            new KeyValuePair<string, object?>(CacheHitTag, cacheHit));

    /// <summary>
    /// Records one request whose URL resolved to nothing (spec section 24.1, task P3-28).
    /// </summary>
    /// <remarks>
    /// Untagged, and specifically not tagged by URL. The whole population of interest is
    /// attacker- and crawler-supplied strings, so a tag here is an unbounded cardinality hole with
    /// an open door in front of it. Which URLs missed is what <c>NotFoundLog</c> is for; this
    /// counter answers the different question of whether the rate has changed.
    /// </remarks>
    public void RecordRouteMiss() => _routeResolutionMiss.Add(1);

    /// <summary>
    /// Records one rendition encode (task P5-32, spec section 24.1).
    /// </summary>
    /// <param name="format">The output format, such as <c>webp</c>.</param>
    /// <param name="elapsed">How long the encode took.</param>
    /// <remarks>
    /// Only actual encodes are counted, never the far more numerous requests served from storage.
    /// That is what makes the counter mean something operationally: a rising rate is renditions
    /// being regenerated — a mass library edit, a lost store, a cache key that changes when it should
    /// not — rather than the site merely getting more traffic.
    /// <para>
    /// Tagged by format and by nothing else. The item id would be one time series per image, which
    /// is the same unbounded-cardinality trap the route-miss counter avoids.
    /// </para>
    /// </remarks>
    public void RecordRenditionGenerated(string format, TimeSpan elapsed)
    {
        var tag = new KeyValuePair<string, object?>(FormatTag, format);

        _renditionGenerated.Add(1, tag);
        _renditionDuration.Record(elapsed.TotalMilliseconds, tag);
    }
}
