namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A standing instruction to publish or unpublish a page at a stated moment (spec section 11.6).
/// </summary>
/// <remarks>
/// The schedule is a row rather than a scan of <c>PageVersion.PublishOn</c> for two reasons that both
/// matter in production. It is <em>claimable</em>: <c>PublishSchedulerService</c> takes a job with an
/// atomic <c>UPDATE … OUTPUT</c>, so two instances polling the same table cannot both publish the
/// same page (risk R16, criterion P7 #7). And it is <em>answerable</em> — "why did this not go live"
/// has an answer with a timestamp and a reason on it, which a nullable column on a version does not.
/// <para>
/// Written by a background service rather than by a person, and therefore excluded from audit
/// capture; the publish it performs is audited by the ordinary path, which is the event worth
/// keeping.
/// </para>
/// </remarks>
public class ScheduledJob : FingerPrintEntityBase
{
    /// <summary>Page the job acts on.</summary>
    public int PageId { get; set; }

    /// <summary>Page the job acts on.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>
    /// The exact version to publish, or null for an unpublish, which is about the page.
    /// </summary>
    public int? PageVersionId { get; set; }

    /// <summary>The exact version to publish.</summary>
    public PageVersion? PageVersion { get; set; }

    /// <summary>Whether the job publishes or retires.</summary>
    public ScheduledJobKind Kind { get; set; }

    /// <summary>When it should happen. Stored UTC, like every instant in this schema.</summary>
    public DateTimeOffset RunOn { get; set; }

    /// <summary>Where it has got to.</summary>
    public ScheduledJobState State { get; set; }

    /// <summary>
    /// Who to tell when it succeeds or fails, and whose identity the publish runs under.
    /// </summary>
    /// <remarks>
    /// A scheduled publish is still somebody's publish. Running it as nobody would leave an audit
    /// row attributed to user 0 and would give the service-layer permission checks no caller to
    /// evaluate — so the editor who scheduled it is carried on the job and reinstated when it runs.
    /// </remarks>
    public int OwnerUserId { get; set; }

    /// <summary>Who to tell when it succeeds or fails.</summary>
    public User Owner { get; set; } = null!;

    /// <summary>When an instance claimed the job, or null while it is unclaimed.</summary>
    public DateTimeOffset? ClaimedOn { get; set; }

    /// <summary>
    /// Which instance holds the claim, for diagnosing a job that was claimed and never finished.
    /// </summary>
    public string? ClaimedBy { get; set; }

    /// <summary>When it finished, either way.</summary>
    public DateTimeOffset? CompletedOn { get; set; }

    /// <summary>Why it failed, in the words the owner is shown.</summary>
    public string? FailureReason { get; set; }
}
