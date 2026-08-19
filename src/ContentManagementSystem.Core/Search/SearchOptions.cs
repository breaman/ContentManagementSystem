namespace ContentManagementSystem.Core.Search;

/// <summary>
/// How search is indexed and queried (task P8-18, spec section 17.1).
/// </summary>
public sealed class SearchOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:Search";

    /// <summary>
    /// Whether the full-text engine is used, or null to ask the server.
    /// </summary>
    /// <remarks>
    /// Null is the setting a deployment should keep: the answer is a property of the instance the
    /// connection string points at, and the probe asks it rather than trusting a file. It exists as
    /// an override for the two cases a probe cannot cover — forcing the fallback while a catalog is
    /// being rebuilt, and asserting the full-text path in a test that must not silently pass on an
    /// engine that has none.
    /// </remarks>
    public bool? UseFullText { get; set; }

    /// <summary>Most hits one query returns.</summary>
    public int MaxResults { get; set; } = 50;

    /// <summary>Hits returned when a caller asks for no particular number.</summary>
    public int DefaultResults { get; set; } = 25;

    /// <summary>Characters of body text shown under a result.</summary>
    public int ExcerptLength { get; set; } = 240;

    /// <summary>Whether this instance runs the nightly reconcile.</summary>
    /// <remarks>
    /// On by default, and harmless on several instances at once — the pass is a comparison that
    /// rewrites only what is wrong. It is a switch rather than a constant so that a deployment which
    /// would rather run it as a job can turn it off here without the sweep silently not happening.
    /// </remarks>
    public bool ReconcileEnabled { get; set; } = true;

    /// <summary>Hours between reconcile passes.</summary>
    /// <remarks>
    /// Nightly, from task P8-18. It is a period rather than a time of day because the pass costs one
    /// indexed scan per kind and has no reason to wait for a quiet hour — and a fixed hour is the
    /// setting that turns out to be the wrong time zone.
    /// </remarks>
    public int ReconcileHours { get; set; } = 24;

    /// <summary>How long after startup the first reconcile pass runs.</summary>
    /// <remarks>
    /// Not immediately. A deployment restarting several instances at once would otherwise have all
    /// of them scan the content tables in the same second, on top of whatever caused the restart.
    /// </remarks>
    public int ReconcileStartupDelayMinutes { get; set; } = 10;
}
