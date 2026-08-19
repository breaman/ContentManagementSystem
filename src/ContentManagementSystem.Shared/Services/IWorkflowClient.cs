using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// The backoffice's view of review, comments, scheduling, notifications, and the audit log
/// (tasks P7-12, P7-16, P7-19, P7-20).
/// </summary>
/// <remarks>
/// One client rather than five, because the screens that use them are one screen: the review panel
/// shows the workflow state, the comment thread, and the schedule side by side, and splitting the
/// contract would mean three injected services to render one card.
/// <para>
/// Implemented twice, like every other backoffice client: over <c>HttpClient</c> in WebAssembly and
/// directly over the services on the server, so a screen pre-renders with its content rather than
/// with a spinner.
/// </para>
/// </remarks>
public interface IWorkflowClient
{
    /// <summary>Reads where a page stands in review.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The state, or null when the page cannot be read.</returns>
    Task<PageWorkflowState?> GetWorkflowAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>Submits the current draft for review.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">Who to ask, by when, and why.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>The new state, or null when the request was refused.</returns>
    Task<PageWorkflowState?> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Approves what is under review.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">Any note to leave with the decision.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>The new state, or null when the decision was refused.</returns>
    Task<PageWorkflowState?> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Sends what is under review back to its author.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">Why it is going back.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>The new state, or null when the decision was refused.</returns>
    Task<PageWorkflowState?> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the review requests waiting on the caller.</summary>
    /// <param name="assignedToMe">Whether to leave out the unaddressed ones.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The requests, or an empty list when the caller approves nothing.</returns>
    Task<IReadOnlyList<WorkflowTaskSummary>> GetTasksAsync(
        bool assignedToMe = false,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a page's comment threads.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The threads, oldest first.</returns>
    Task<IReadOnlyList<CommentSummary>> GetCommentsAsync(
        int pageId,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a remark or a reply.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">What to say and what it is about.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>The comment as stored, or null when it was refused.</returns>
    Task<CommentSummary?> AddCommentAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a thread dealt with, or reopens it.</summary>
    /// <param name="commentId">Identity of the thread's root comment.</param>
    /// <param name="resolved">Whether it is now dealt with.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>The comment as it now stands, or null when the change was refused.</returns>
    Task<CommentSummary?> ResolveCommentAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default);

    /// <summary>Reads what is scheduled for a page.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The schedule, or null when the page cannot be read.</returns>
    Task<PageScheduleState?> GetScheduleAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>Sets, changes, or clears a page's schedule.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">The instants wanted, either of which may be null to cancel.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>The schedule as it now stands, or null when it was refused.</returns>
    Task<PageScheduleState?> SetScheduleAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the caller's own inbox.</summary>
    /// <param name="unreadOnly">Whether to leave out what has been read.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The inbox and its unread count.</returns>
    Task<NotificationInbox?> GetNotificationsAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default);

    /// <summary>Marks one notification read, or every unread one.</summary>
    /// <param name="notificationId">The one to mark, or null for all of them.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>How many were marked.</returns>
    Task<int> MarkNotificationsReadAsync(
        int? notificationId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a page of the audit log.</summary>
    /// <param name="query">Which entries.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>The entries and the cursor for the next page, or null when the caller may not read it.</returns>
    Task<CursorPage<AuditEntrySummary>?> GetAuditAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
