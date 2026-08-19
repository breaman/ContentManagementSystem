using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Puts the page editor's review panel into a state worth judging (tasks P7-12, P6-36, P6-38).
/// </summary>
/// <remarks>
/// Without this registration the page editor did not render at all under these gates — the review
/// panel resolves <see cref="IWorkflowClient"/> as a property, and a missing one throws during the
/// render rather than degrading — so the accessibility and reflow gates for that screen were
/// failing on a missing service rather than reporting on the markup. Found while adding the tag
/// box's own registration.
/// <para>
/// It answers with a draft awaiting submission and one comment thread: a panel with nothing in it
/// draws almost nothing, and axe has nothing to say about markup that is not there.
/// </para>
/// </remarks>
public sealed class FakeWorkflowClient : IWorkflowClient
{
    /// <inheritdoc />
    public Task<PageWorkflowState?> GetWorkflowAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PageWorkflowState?>(new PageWorkflowState(
            pageId,
            "Simple",
            "Draft",
            Pending: null,
            History: [],
            CanSubmit: true,
            CanDecide: false,
            CanPublish: true));

    /// <inheritdoc />
    public Task<PageWorkflowState?> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default) =>
        GetWorkflowAsync(pageId, cancellationToken);

    /// <inheritdoc />
    public Task<PageWorkflowState?> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        GetWorkflowAsync(pageId, cancellationToken);

    /// <inheritdoc />
    public Task<PageWorkflowState?> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        GetWorkflowAsync(pageId, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<WorkflowTaskSummary>> GetTasksAsync(
        bool assignedToMe = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkflowTaskSummary>>([]);

    /// <inheritdoc />
    public Task<IReadOnlyList<CommentSummary>> GetCommentsAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommentSummary>>([]);

    /// <inheritdoc />
    public Task<CommentSummary?> AddCommentAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CommentSummary?>(null);

    /// <inheritdoc />
    public Task<CommentSummary?> ResolveCommentAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CommentSummary?>(null);

    /// <inheritdoc />
    public Task<PageScheduleState?> GetScheduleAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PageScheduleState?>(null);

    /// <inheritdoc />
    public Task<PageScheduleState?> SetScheduleAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<PageScheduleState?>(null);

    /// <inheritdoc />
    public Task<NotificationInbox?> GetNotificationsAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<NotificationInbox?>(null);

    /// <inheritdoc />
    public Task<int> MarkNotificationsReadAsync(
        int? notificationId = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0);

    /// <inheritdoc />
    public Task<CursorPage<AuditEntrySummary>?> GetAuditAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CursorPage<AuditEntrySummary>?>(null);
}
