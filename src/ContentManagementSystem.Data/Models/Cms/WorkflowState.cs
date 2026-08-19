namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// Where a review request has got to (spec section 11.9).
/// </summary>
public enum WorkflowState
{
    /// <summary>Submitted and waiting on an approver.</summary>
    Pending = 0,

    /// <summary>An approver accepted it. The version is <c>Approved</c> and may be published.</summary>
    Approved = 1,

    /// <summary>An approver sent it back. The content is copied into a fresh draft.</summary>
    Rejected = 2,

    /// <summary>
    /// Withdrawn before anybody decided — the author resubmitted, or the page was deleted.
    /// </summary>
    /// <remarks>
    /// Superseded requests are cancelled rather than deleted so that "who asked for this, and when"
    /// survives a resubmission. An inbox filters these out; the history does not.
    /// </remarks>
    Cancelled = 3,
}
