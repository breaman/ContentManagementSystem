using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// An <see cref="IWorkflowClient"/> that reports nothing to review, nothing scheduled, and no
/// comments.
/// </summary>
/// <remarks>
/// Unlike <see cref="StubPageClient"/> and its siblings, this answers rather than throws. The review,
/// schedule, and comment panels are rendered by the page editor on every screen, so a stub that
/// refused would turn every editor test into a test of those three panels. Answering "nothing" is
/// also what the panels are built to draw nothing for, which keeps a suite about saving a draft
/// looking at the markup it is about.
/// <para>
/// A test that <em>is</em> about review overrides the members it cares about.
/// </para>
/// </remarks>
public class SilentWorkflowClient : IWorkflowClient
{
    /// <inheritdoc />
    public virtual Task<PageWorkflowState?> GetWorkflowAsync(
        int pageId,
        CancellationToken cancellationToken = default) => Task.FromResult<PageWorkflowState?>(null);

    /// <inheritdoc />
    public virtual Task<PageWorkflowState?> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult<PageWorkflowState?>(null);

    /// <inheritdoc />
    public virtual Task<PageWorkflowState?> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult<PageWorkflowState?>(null);

    /// <inheritdoc />
    public virtual Task<PageWorkflowState?> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult<PageWorkflowState?>(null);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<WorkflowTaskSummary>> GetTasksAsync(
        bool assignedToMe = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkflowTaskSummary>>([]);

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<CommentSummary>> GetCommentsAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CommentSummary>>([]);

    /// <inheritdoc />
    public virtual Task<CommentSummary?> AddCommentAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult<CommentSummary?>(null);

    /// <inheritdoc />
    public virtual Task<CommentSummary?> ResolveCommentAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default) => Task.FromResult<CommentSummary?>(null);

    /// <inheritdoc />
    public virtual Task<PageScheduleState?> GetScheduleAsync(
        int pageId,
        CancellationToken cancellationToken = default) => Task.FromResult<PageScheduleState?>(null);

    /// <inheritdoc />
    public virtual Task<PageScheduleState?> SetScheduleAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default) => Task.FromResult<PageScheduleState?>(null);

    /// <inheritdoc />
    public virtual Task<NotificationInbox?> GetNotificationsAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default) => Task.FromResult<NotificationInbox?>(null);

    /// <inheritdoc />
    public virtual Task<int> MarkNotificationsReadAsync(
        int? notificationId = null,
        CancellationToken cancellationToken = default) => Task.FromResult(0);

    /// <inheritdoc />
    public virtual Task<CursorPage<AuditEntrySummary>?> GetAuditAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<CursorPage<AuditEntrySummary>?>(null);
}
