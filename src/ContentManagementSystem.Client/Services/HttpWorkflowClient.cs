using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IWorkflowClient"/>, over the management API
/// (tasks P7-12, P7-16, P7-19, P7-20).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Writes carry the antiforgery token, fetched once per client instance, exactly as
/// <see cref="HttpPageClient"/> does.
/// <para>
/// A refusal comes back as <see langword="null"/> rather than as an exception. Every one of these
/// calls is behind a button whose enabled state the server already decided — the three <c>Can…</c>
/// flags on <see cref="PageWorkflowState"/> — so a refusal here means the state moved under the
/// editor, and the screen's answer is to reload it rather than to show a stack trace.
/// </para>
/// </remarks>
public sealed class HttpWorkflowClient(HttpClient http) : IWorkflowClient
{
    private const string Base = "api/cms/v1";

    private AntiforgeryTokenResponse? _token;

    /// <inheritdoc />
    public Task<PageWorkflowState?> GetWorkflowAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        GetAsync<PageWorkflowState>($"{Base}/pages/{pageId}/workflow", cancellationToken);

    /// <inheritdoc />
    public Task<PageWorkflowState?> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<SubmitForReviewRequest, PageWorkflowState>(
            $"{Base}/pages/{pageId}/submit",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<PageWorkflowState?> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<WorkflowDecisionRequest, PageWorkflowState>(
            $"{Base}/pages/{pageId}/approve",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<PageWorkflowState?> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<WorkflowDecisionRequest, PageWorkflowState>(
            $"{Base}/pages/{pageId}/reject",
            request,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WorkflowTaskSummary>> GetTasksAsync(
        bool assignedToMe = false,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<WorkflowTaskSummary>>(
            $"{Base}/workflow/tasks?assignedToMe={(assignedToMe ? "true" : "false")}",
            cancellationToken) ?? [];

    /// <inheritdoc />
    public async Task<IReadOnlyList<CommentSummary>> GetCommentsAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        await GetAsync<List<CommentSummary>>(
            $"{Base}/pages/{pageId}/comments",
            cancellationToken) ?? [];

    /// <inheritdoc />
    public Task<CommentSummary?> AddCommentAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<CreateCommentRequest, CommentSummary>(
            $"{Base}/pages/{pageId}/comments",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<CommentSummary?> ResolveCommentAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default) =>
        PostAsync<object, CommentSummary>(
            $"{Base}/comments/{commentId}/resolve?resolved={(resolved ? "true" : "false")}",
            null,
            cancellationToken);

    /// <inheritdoc />
    public Task<PageScheduleState?> GetScheduleAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        GetAsync<PageScheduleState>($"{Base}/pages/{pageId}/schedule", cancellationToken);

    /// <inheritdoc />
    public Task<PageScheduleState?> SetScheduleAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<SetScheduleRequest, PageScheduleState>(
            $"{Base}/pages/{pageId}/schedule",
            request,
            cancellationToken);

    /// <inheritdoc />
    public Task<NotificationInbox?> GetNotificationsAsync(
        bool unreadOnly = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<NotificationInbox>(
            $"{Base}/notifications?unreadOnly={(unreadOnly ? "true" : "false")}",
            cancellationToken);

    /// <inheritdoc />
    public async Task<int> MarkNotificationsReadAsync(
        int? notificationId = null,
        CancellationToken cancellationToken = default)
    {
        var path = notificationId is { } id
            ? $"{Base}/notifications/read?id={id}"
            : $"{Base}/notifications/read";

        return await PostAsync<object, int>(path, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CursorPage<AuditEntrySummary>?> GetAuditAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parameters = new List<string>
        {
            $"limit={query.Limit.ToString(CultureInfo.InvariantCulture)}",
        };

        if (!string.IsNullOrWhiteSpace(query.Entity)) parameters.Add($"entity={Uri.EscapeDataString(query.Entity)}");
        if (!string.IsNullOrWhiteSpace(query.EntityId)) parameters.Add($"entityId={Uri.EscapeDataString(query.EntityId)}");
        if (query.UserId is { } userId) parameters.Add($"userId={userId}");
        if (query.From is { } from) parameters.Add($"from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}");
        if (query.To is { } to) parameters.Add($"to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}");
        if (!string.IsNullOrWhiteSpace(query.Cursor)) parameters.Add($"cursor={Uri.EscapeDataString(query.Cursor)}");

        return GetAsync<CursorPage<AuditEntrySummary>>(
            $"{Base}/audit?{string.Join('&', parameters)}",
            cancellationToken);
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, cancellationToken);

        // Forbidden is a state to draw, not a fault: a screen with a review panel on it is opened
        // by people who cannot review, and the panel's answer is to show nothing.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent
            or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }

    private async Task<TResult?> PostAsync<TBody, TResult>(
        string path,
        TBody? body,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null) request.Content = JsonContent.Create(body);

        var token = await TokenAsync(cancellationToken);

        request.Headers.Add(token.HeaderName, token.RequestToken);

        var response = await http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) return default;

        return await response.Content.ReadFromJsonAsync<TResult>(cancellationToken);
    }

    private async Task<AntiforgeryTokenResponse> TokenAsync(CancellationToken cancellationToken) =>
        _token ??= await http.GetFromJsonAsync<AntiforgeryTokenResponse>(
            $"{Base}/antiforgery-token",
            cancellationToken) ?? throw new InvalidOperationException(
                "The server did not issue an antiforgery token, so no review action can be sent.");
}
