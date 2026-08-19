using ContentManagementSystem.Core.Auditing;
using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Core.Scheduling;
using ContentManagementSystem.Core.Workflow;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IWorkflowClient"/>, over the services directly
/// (tasks P7-12, P7-16, P7-19, P7-20).
/// </summary>
/// <param name="workflow">Submit, approve, reject, and the inbox.</param>
/// <param name="comments">Threaded review remarks.</param>
/// <param name="scheduling">Publish and retirement times.</param>
/// <param name="notifications">The in-app inbox.</param>
/// <param name="audit">The audit log.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used while pre-rendering, so a review panel arrives filled in rather than as a spinner. Each
/// service authorizes the caller itself against the same request principal the API would have seen,
/// so the shortcut changes nothing about who sees what.
/// <para>
/// Every write here returns the refusal as <see langword="null"/>, matching the WebAssembly half.
/// Writes do not in fact happen during pre-render — there is no user gesture yet — but the two
/// halves have to be interchangeable, and a server half that threw where the client half returned
/// null would be a difference that only showed up under pre-rendering.
/// </para>
/// </remarks>
public sealed class ServerWorkflowClient(
    IWorkflowService workflow,
    ICommentService comments,
    ISchedulingService scheduling,
    INotificationService notifications,
    IAuditQueryService audit,
    PrerenderGate gate) : IWorkflowClient
{
    /// <inheritdoc />
    public async Task<PageWorkflowState?> GetWorkflowAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => workflow.GetAsync(pageId, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<PageWorkflowState?> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => workflow.SubmitAsync(pageId, request, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<PageWorkflowState?> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => workflow.ApproveAsync(pageId, request, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<PageWorkflowState?> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => workflow.RejectAsync(pageId, request, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowTaskSummary>> GetTasksAsync(
        bool assignedToMe = false,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => workflow.InboxAsync(assignedToMe, 50, token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommentSummary>> GetCommentsAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => comments.ListAsync(pageId, null, true, token),
            cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<CommentSummary?> AddCommentAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => comments.AddAsync(pageId, request, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<CommentSummary?> ResolveCommentAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => comments.ResolveAsync(commentId, resolved, token),
            cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<PageScheduleState?> GetScheduleAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => scheduling.GetAsync(pageId, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<PageScheduleState?> SetScheduleAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => scheduling.SetAsync(pageId, request, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<NotificationInbox?> GetNotificationsAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => notifications.InboxAsync(unreadOnly, 50, token),
            cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<int> MarkNotificationsReadAsync(
        int? notificationId = null,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => notifications.MarkReadAsync(notificationId, token),
            cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<CursorPage<AuditEntrySummary>?> GetAuditAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => audit.ListAsync(query, token), cancellationToken)).Value;
}
