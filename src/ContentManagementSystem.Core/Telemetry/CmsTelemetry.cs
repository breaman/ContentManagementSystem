using System.Diagnostics;

namespace ContentManagementSystem.Core.Telemetry;

/// <summary>
/// The names the CMS publishes telemetry under (task P2-29, spec section 24.1).
/// </summary>
/// <remarks>
/// Constants rather than string literals at each call site, because these names are a contract with
/// whatever is collecting them: a dashboard, an alert, and a query all break silently when a name
/// changes, and none of them is in this repository to break loudly.
/// <para>
/// One name for the meter and the activity source. They are separate registrations in
/// OpenTelemetry — <c>AddMeter</c> and <c>AddSource</c> — but they identify the same component, and
/// two spellings of it would make a collector configuration that captures the metrics while silently
/// dropping the traces.
/// </para>
/// </remarks>
public static class CmsTelemetry
{
    /// <summary>Name of the meter every CMS metric is published on (spec section 24.1).</summary>
    public const string MeterName = "ContentManagementSystem.Cms";

    /// <summary>Name of the activity source every CMS span is started from.</summary>
    public const string ActivitySourceName = "ContentManagementSystem.Cms";

    /// <summary>Name of the span covering one publish attempt.</summary>
    public const string PublishActivityName = "cms.publish";

    /// <summary>Tag naming the page an operation acted on.</summary>
    public const string PageIdTag = "cms.page.id";

    /// <summary>Tag carrying the version number a publish produced.</summary>
    public const string VersionNumberTag = "cms.version.number";

    /// <summary>
    /// How a publish attempt ended, as the <c>result</c> tag spells it.
    /// </summary>
    /// <remarks>
    /// A closed set of low-cardinality values. A tag whose values come from user input or from an
    /// exception message turns one time series into thousands, which is how a metrics bill and a
    /// collector both fall over.
    /// </remarks>
    public static class PublishResults
    {
        /// <summary>The draft was snapshotted and the page repointed.</summary>
        public const string Published = "published";

        /// <summary>Validation refused it, or warnings were not acknowledged.</summary>
        public const string Refused = "refused";

        /// <summary>The caller does not hold <c>Content.Publish</c>.</summary>
        public const string Forbidden = "forbidden";

        /// <summary>There is no such page.</summary>
        public const string NotFound = "not-found";

        /// <summary>
        /// It threw, so the transaction rolled back.
        /// </summary>
        /// <remarks>
        /// The one value worth alerting on. A refusal is an editor being told something; this is the
        /// publish path itself failing, which is risk R4 and has no repair path that does not begin
        /// with somebody noticing.
        /// </remarks>
        public const string Failed = "failed";
    }

    /// <summary>
    /// The source CMS spans are started from.
    /// </summary>
    /// <remarks>
    /// Static and never disposed. An <see cref="ActivitySource"/> is process-wide by design, and its
    /// cost when nothing is listening is a null check — <c>StartActivity</c> returns null, so the
    /// instrumented code pays for a branch rather than for an allocation.
    /// </remarks>
    public static ActivitySource Source { get; } = new(ActivitySourceName);
}
