namespace ContentManagementSystem.Core.Auditing;

/// <summary>
/// When the nightly retention sweep runs (task P9-25, spec section 11.7).
/// </summary>
/// <remarks>
/// <strong>How long things are kept is not here.</strong> Both windows — versions and audit rows —
/// live in <c>SiteSettings</c>, where an administrator edits them and where a legal answer to
/// <strong>Q9</strong> lands as a number rather than as a deployment change. What is configurable
/// here is only the shape of the loop: whether this instance runs it, how often, and how long it
/// waits first.
/// </remarks>
public sealed class RetentionOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:Retention";

    /// <summary>
    /// Whether this instance runs the nightly sweep.
    /// </summary>
    /// <remarks>
    /// On by default, and safe on several instances at once: both sweeps are idempotent, and the
    /// audit one deletes in batches by primary key, so two of them racing delete disjoint rows rather
    /// than colliding. It is a switch rather than a constant so a deployment that would rather run
    /// this as a scheduled job can turn it off here — deliberately, and visibly — instead of the
    /// sweep quietly not happening.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Hours between sweeps, clamped to a day at most.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// Minutes to wait after startup before the first sweep.
    /// </summary>
    /// <remarks>
    /// Not at startup, for the reason the search reconcile is not: several instances coming back at
    /// once would otherwise all start deleting in the same second, on top of whatever restarted them.
    /// </remarks>
    public int StartupDelayMinutes { get; set; } = 20;
}
