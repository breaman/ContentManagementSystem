namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One request for a page version to be reviewed, and the decision it eventually received
/// (spec section 11.9).
/// </summary>
/// <remarks>
/// The row exists so that "waiting on me" is a query rather than a scan of every page's status, and
/// so that a decision is attributable after the version it was about has been superseded. Status
/// lives on <see cref="PageVersion"/>; <em>who asked whom, when, and what they said</em> lives here.
/// <para>
/// A version has at most one <see cref="WorkflowState.Pending"/> task at a time — enforced by a
/// filtered unique index — but any number of settled ones, because resubmitting after a rejection is
/// the ordinary path and each round is worth keeping.
/// </para>
/// </remarks>
public class WorkflowTask : FingerPrintEntityBase
{
    /// <summary>Page under review.</summary>
    public int PageId { get; set; }

    /// <summary>Page under review.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>The exact version submitted, not the page's current draft.</summary>
    public int PageVersionId { get; set; }

    /// <summary>The exact version submitted.</summary>
    public PageVersion PageVersion { get; set; } = null!;

    /// <summary>
    /// The approver the request was addressed to, or null when it was addressed to all of them.
    /// </summary>
    /// <remarks>
    /// Nullable because <see cref="WorkflowMode.Simple"/> means "any approver may take this", and
    /// inventing an assignee to satisfy the column would put the request in one person's inbox and
    /// out of everybody else's — which is the opposite of what that mode is for.
    /// </remarks>
    public int? AssignedToUserId { get; set; }

    /// <summary>The approver the request was addressed to.</summary>
    public User? AssignedTo { get; set; }

    /// <summary>Where the request has got to.</summary>
    public WorkflowState State { get; set; }

    /// <summary>When the author would like a decision. Advisory; nothing enforces it.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>What the author said when submitting.</summary>
    public string? SubmissionNote { get; set; }

    /// <summary>Who decided, once somebody has.</summary>
    public int? DecidedByUserId { get; set; }

    /// <summary>Who decided.</summary>
    public User? DecidedBy { get; set; }

    /// <summary>When the decision was made.</summary>
    public DateTimeOffset? DecidedOn { get; set; }

    /// <summary>
    /// Why it was rejected, or any note left on an approval.
    /// </summary>
    /// <remarks>
    /// Kept alongside the threaded <see cref="Comment"/>s rather than instead of them. A rejection
    /// reason has to survive as one field the author is shown on the way back into the draft;
    /// comments are the conversation that led there and are anchored to particular zones.
    /// </remarks>
    public string? DecisionNote { get; set; }
}
