using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Core.Workflow;

/// <summary>
/// Submit, approve, and reject — the three editorial acts that stand between a finished draft and a
/// live page (tasks P7-09 to P7-11, spec section 11.9).
/// </summary>
/// <remarks>
/// The mode is site-wide in v1 and read from <c>SiteSettings</c> on every call rather than cached:
/// switching a site from <c>None</c> to <c>TwoStep</c> has to take effect on the next request, not
/// when a process recycles.
/// <list type="table">
/// <item>
/// <term><c>None</c></term>
/// <description>There is nothing to submit to. Anyone holding <c>Content.Publish</c> publishes.</description>
/// </item>
/// <item>
/// <term><c>Simple</c></term>
/// <description>An author submits; any approver may approve, and approving is enough to let the
/// publish through.</description>
/// </item>
/// <item>
/// <term><c>TwoStep</c></term>
/// <description>Submit, approve, and publish are three acts, and the approver may not be the person
/// who submitted. Publishing an unapproved version is refused however senior the caller
/// (criterion P7 #3).</description>
/// </item>
/// </list>
/// <para>
/// <strong>The draft is locked while it is under review.</strong> That is not politeness: the whole
/// value of an approval is that what goes live is what was approved, and a draft that could be
/// edited between the two would make the approval a statement about content that no longer exists.
/// <c>DraftService</c> refuses saves against an <c>InReview</c> version for that reason.
/// </para>
/// </remarks>
public interface IWorkflowService
{
    /// <summary>Reports where a page stands, and what this caller may do about it.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The mode, the open request if any, the settled history, and three capability flags.</returns>
    Task<CmsResult<PageWorkflowState>> GetAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>Puts the current draft in front of an approver.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">Who to ask, by when, and why.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The page's new workflow state.</returns>
    Task<CmsResult<PageWorkflowState>> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Accepts a submission, so the version may be published.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">Any note to leave with the decision.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The page's new workflow state.</returns>
    Task<CmsResult<PageWorkflowState>> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a submission back, and returns the content to a fresh, editable draft.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">Why it is being sent back. Strongly expected, though not required.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The page's new workflow state.</returns>
    /// <remarks>
    /// The rejected version is kept as a <c>Rejected</c> row and its content is copied into a new
    /// draft, rather than the same row being unlocked. That keeps the timeline forward-moving — the
    /// thing that was refused stays exactly as it was refused — and it is what criterion P7 #4
    /// asserts, along with the comments, which belong to the page and are therefore untouched.
    /// </remarks>
    Task<CmsResult<PageWorkflowState>> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists review requests waiting on the caller.</summary>
    /// <param name="assignedToMe">
    /// Whether to return only the requests addressed to the caller by name. False also returns the
    /// unassigned ones, which in <c>Simple</c> mode is most of them.
    /// </param>
    /// <param name="limit">Most rows to return.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The open requests, oldest first — an inbox is a queue.</returns>
    Task<CmsResult<IReadOnlyList<WorkflowTaskSummary>>> InboxAsync(
        bool assignedToMe = false,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
