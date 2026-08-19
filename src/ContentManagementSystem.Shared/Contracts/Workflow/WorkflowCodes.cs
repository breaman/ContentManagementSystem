namespace ContentManagementSystem.Shared.Contracts.Workflow;

/// <summary>
/// Problem codes the workflow and comment endpoints emit (spec section 11.9).
/// </summary>
/// <remarks>
/// Separate from <c>PageCodes</c> because a client acts on these differently: every one of them
/// describes the <em>state of a review</em> rather than the state of the content, and the editor's
/// screen responds to them by changing which buttons it offers rather than by pointing at a field.
/// </remarks>
public static class WorkflowCodes
{
    /// <summary>The caller may not do this at all, or not here.</summary>
    public const string Forbidden = "workflow.forbidden";

    /// <summary>No page, version, task, or comment has that id.</summary>
    public const string NotFound = "workflow.not-found";

    /// <summary>The site is in <c>None</c> mode, where there is nothing to submit to.</summary>
    public const string WorkflowDisabled = "workflow.disabled";

    /// <summary>The draft is already under review, so there is nothing to submit.</summary>
    public const string AlreadySubmitted = "workflow.already-submitted";

    /// <summary>Nothing is under review, so there is nothing to approve or reject.</summary>
    public const string NothingToDecide = "workflow.nothing-to-decide";

    /// <summary>
    /// The caller submitted this and the site runs <c>TwoStep</c>, where they may not approve it.
    /// </summary>
    public const string SelfApproval = "workflow.self-approval";

    /// <summary>Publishing was refused because the version has not been approved.</summary>
    public const string ApprovalRequired = "workflow.approval-required";

    /// <summary>The draft is locked while it is under review and cannot be edited.</summary>
    public const string LockedForReview = "workflow.locked-for-review";

    /// <summary>A comment arrived with nothing in it, or with more than the column holds.</summary>
    public const string CommentInvalid = "workflow.comment-invalid";

    /// <summary>The comment being replied to belongs to a different page.</summary>
    public const string CommentMismatch = "workflow.comment-mismatch";

    /// <summary>The person a review was addressed to cannot approve anything.</summary>
    public const string AssigneeInvalid = "workflow.assignee-invalid";
}
