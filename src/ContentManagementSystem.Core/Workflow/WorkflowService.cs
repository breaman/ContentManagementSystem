using System.Linq.Expressions;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Workflow;

/// <inheritdoc cref="IWorkflowService" />
/// <param name="context">The application database context.</param>
/// <param name="references">Rewrites a new draft's reference rows from its payload.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (spec section 21.2).</param>
/// <param name="users">Who the caller is, which is who a decision is attributed to.</param>
/// <param name="notifications">Tells the people waiting on each step that it happened.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for every submission and decision.</param>
public sealed class WorkflowService(
    ApplicationDbContext context,
    IContentReferenceProjector references,
    ICmsAuthorization authorization,
    IAclService acl,
    IUserService users,
    INotificationService notifications,
    TimeProvider clock,
    ILogger<WorkflowService> logger) : IWorkflowService
{
    /// <summary>How many settled requests a page's history returns.</summary>
    private const int HistoryLimit = 20;

    /// <summary>Largest inbox page, whatever was asked for.</summary>
    private const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<CmsResult<PageWorkflowState>> GetAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden("Reading pages is not permitted.");
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return NotFound(pageId);
        }

        var page = await LoadAsync(pageId, tracked: false, cancellationToken);

        if (page?.DraftVersion is null) return NotFound(pageId);

        return CmsResult<PageWorkflowState>.Success(await ProjectAsync(page, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageWorkflowState>> SubmitAsync(
        int pageId,
        SubmitForReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentSubmit))
        {
            return Forbidden("Submitting pages for review is not permitted.");
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentEdit, pageId, cancellationToken))
        {
            return Forbidden($"Editing page {pageId} is not permitted.");
        }

        var mode = await ModeAsync(cancellationToken);

        if (mode is WorkflowMode.None)
        {
            return CmsResult<PageWorkflowState>.Invalid(
                WorkflowCodes.WorkflowDisabled,
                "This site publishes without approval, so there is nothing to submit to.");
        }

        var page = await LoadAsync(pageId, tracked: true, cancellationToken);

        if (page?.DraftVersion is null) return NotFound(pageId);

        var draft = page.DraftVersion;

        if (draft.Status is PageVersionStatus.InReview)
        {
            return CmsResult<PageWorkflowState>.Conflict(
                WorkflowCodes.AlreadySubmitted,
                "This draft is already waiting for review.");
        }

        if (request.AssignedToUserId is { } assignee
            && !await CanDecideAsync(assignee, cancellationToken))
        {
            return CmsResult<PageWorkflowState>.Invalid(
                WorkflowCodes.AssigneeInvalid,
                "That person cannot approve content, so a review addressed to them would never be " +
                "picked up.",
                nameof(SubmitForReviewRequest.AssignedToUserId));
        }

        // Any earlier round is settled rather than left open. Two pending requests for one page is a
        // queue two people can each believe they own.
        await CancelOpenAsync(pageId, cancellationToken);

        var task = new WorkflowTask
        {
            PageId = pageId,
            PageVersionId = draft.Id,
            AssignedToUserId = request.AssignedToUserId,
            State = WorkflowState.Pending,
            DueDate = request.DueDate,
            SubmissionNote = Clean(request.Note, FieldLengths.WorkflowNote),
        };

        context.WorkflowTasks.Add(task);

        // The draft stops being editable here. See IWorkflowService for why that is the point
        // rather than an inconvenience.
        draft.Status = PageVersionStatus.InReview;

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Page {PageId} version {VersionNumber} submitted for review by user {UserId}.",
            pageId,
            draft.VersionNumber,
            users.UserId);

        await notifications.NotifyManyAsync(
            request.AssignedToUserId is { } named ? [named] : await ApproverIdsAsync(cancellationToken),
            NotificationKind.Submitted,
            pageId,
            draft.Title,
            await NameAsync(users.UserId, cancellationToken),
            request.Note,
            $"/admin/pages/{pageId}",
            cancellationToken: cancellationToken);

        return CmsResult<PageWorkflowState>.Success(await ProjectAsync(page, cancellationToken));
    }

    /// <inheritdoc />
    public Task<CmsResult<PageWorkflowState>> ApproveAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        DecideAsync(pageId, request, approved: true, cancellationToken);

    /// <inheritdoc />
    public Task<CmsResult<PageWorkflowState>> RejectAsync(
        int pageId,
        WorkflowDecisionRequest request,
        CancellationToken cancellationToken = default) =>
        DecideAsync(pageId, request, approved: false, cancellationToken);

    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<WorkflowTaskSummary>>> InboxAsync(
        bool assignedToMe = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentApprove))
        {
            return CmsResult<IReadOnlyList<WorkflowTaskSummary>>.Forbidden(
                "Approving content is not permitted.",
                WorkflowCodes.Forbidden);
        }

        var me = users.UserId;

        var query = context.WorkflowTasks
            .AsNoTracking()
            .Where(task => task.State == WorkflowState.Pending);

        query = assignedToMe
            ? query.Where(task => task.AssignedToUserId == me)

            // Unassigned requests are everybody's in Simple mode, so an inbox that showed only the
            // ones addressed by name would be empty on the sites that use the feature most.
            : query.Where(task => task.AssignedToUserId == null || task.AssignedToUserId == me);

        var rows = await query
            .OrderBy(task => task.Id)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .Select(Projection)
            .ToListAsync(cancellationToken);

        // The tree's access rules reach the inbox too: a request to review a page in a branch this
        // approver may not read is a request they could not act on anyway.
        var readable = await acl.GetFilterAsync(CmsPermissions.ContentRead, cancellationToken);

        if (!readable.IsUnrestricted)
        {
            var paths = await PathsAsync([.. rows.Select(row => row.PageId)], cancellationToken);

            rows = [.. rows.Where(row =>
                !paths.TryGetValue(row.PageId, out var path) || readable.Allows(row.PageId, path))];
        }

        return CmsResult<IReadOnlyList<WorkflowTaskSummary>>.Success(rows);
    }

    private async Task<CmsResult<PageWorkflowState>> DecideAsync(
        int pageId,
        WorkflowDecisionRequest request,
        bool approved,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentApprove))
        {
            return Forbidden("Approving content is not permitted.");
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentApprove, pageId, cancellationToken))
        {
            return Forbidden($"Approving content on page {pageId} is not permitted.");
        }

        var page = await LoadAsync(pageId, tracked: true, cancellationToken);

        if (page?.DraftVersion is null) return NotFound(pageId);

        var task = await context.WorkflowTasks
            .Where(candidate => candidate.PageId == pageId && candidate.State == WorkflowState.Pending)
            .OrderByDescending(candidate => candidate.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
        {
            return CmsResult<PageWorkflowState>.Invalid(
                WorkflowCodes.NothingToDecide,
                "Nothing on this page is waiting for review.");
        }

        var me = users.UserId;
        var mode = await ModeAsync(cancellationToken);

        // The separation-of-duties clause, and the only rule in this file that depends on the mode
        // at decision time (criterion P7 #3). Simple mode deliberately allows it: there, submitting
        // is what somebody without publish rights does, and an approver who submitted was never
        // going to be blocked by ceremony they could have skipped.
        if (approved && mode is WorkflowMode.TwoStep && task.CreatedBy == me)
        {
            return CmsResult<PageWorkflowState>.Forbidden(
                "You submitted this, and this site asks for a second pair of eyes before publishing.",
                WorkflowCodes.SelfApproval);
        }

        var now = clock.GetUtcNow();

        task.State = approved ? WorkflowState.Approved : WorkflowState.Rejected;
        task.DecidedByUserId = me;
        task.DecidedOn = now;
        task.DecisionNote = Clean(request.Note, FieldLengths.WorkflowNote);

        var submitted = page.DraftVersion.Id == task.PageVersionId
            ? page.DraftVersion
            : await context.PageVersions.FirstAsync(
                version => version.Id == task.PageVersionId,
                cancellationToken);

        if (approved)
        {
            submitted.Status = PageVersionStatus.Approved;
        }
        else
        {
            await RejectIntoFreshDraftAsync(page, submitted, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Page {PageId} version {VersionId} was {Decision} by user {UserId}.",
            pageId,
            task.PageVersionId,
            approved ? "approved" : "rejected",
            me);

        await notifications.NotifyAsync(
            task.CreatedBy,
            approved ? NotificationKind.Approved : NotificationKind.Rejected,
            pageId,
            submitted.Title,
            await NameAsync(me, cancellationToken),
            request.Note,
            $"/admin/pages/{pageId}",
            cancellationToken: cancellationToken);

        return CmsResult<PageWorkflowState>.Success(await ProjectAsync(page, cancellationToken));
    }

    /// <summary>
    /// Freezes the refused version and hands the author back an editable copy of it.
    /// </summary>
    /// <remarks>
    /// The refused row keeps exactly the content that was refused, which is what makes a rejection
    /// auditable; the copy is what the author carries on from. Comments are not touched — they hang
    /// off the page, so they survive this by construction rather than by being moved
    /// (criterion P7 #4).
    /// </remarks>
    private async Task RejectIntoFreshDraftAsync(
        Page page,
        PageVersion submitted,
        CancellationToken cancellationToken)
    {
        submitted.Status = PageVersionStatus.Rejected;

        var replacement = DraftService.Copy(
            submitted,
            await VersionNumbers.NextAsync(context, page.Id, cancellationToken));
        replacement.Status = PageVersionStatus.Draft;

        context.PageVersions.Add(replacement);

        // Saved before the page is repointed, because the new row needs its identity first — the
        // same two-statement dance the page's very first draft does.
        await context.SaveChangesAsync(cancellationToken);

        page.DraftVersionId = replacement.Id;
        page.DraftVersion = replacement;

        // The reference index is per version, so the fresh draft starts with none. Left unprojected
        // it would look to the delete guards and the where-used screen like a draft that points at
        // nothing.
        if (ContentPayload.TryParse(replacement.ContentJson, out var payload))
        {
            await references.ProjectAsync(
                ContentSourceType.PageVersion,
                replacement.Id,
                payload,
                cancellationToken);
        }
    }

    private async Task<PageWorkflowState> ProjectAsync(Page page, CancellationToken cancellationToken)
    {
        var mode = await ModeAsync(cancellationToken);

        var tasks = await context.WorkflowTasks
            .AsNoTracking()
            .Where(task => task.PageId == page.Id)
            .OrderByDescending(task => task.Id)
            .Take(HistoryLimit)
            .Select(Projection)
            .ToListAsync(cancellationToken);

        var pending = tasks.FirstOrDefault(task => task.State == nameof(WorkflowState.Pending));
        var history = tasks.Where(task => task.State != nameof(WorkflowState.Pending)).ToList();

        var draftStatus = page.DraftVersion?.Status ?? PageVersionStatus.Draft;
        var me = users.UserId;

        var canApprove = authorization.HasPermission(CmsPermissions.ContentApprove)
            && await acl.IsAllowedAsync(CmsPermissions.ContentApprove, page.Id, cancellationToken);

        var canPublish = authorization.HasPermission(CmsPermissions.ContentPublish)
            && await acl.IsAllowedAsync(CmsPermissions.ContentPublish, page.Id, cancellationToken)
            && (mode is not WorkflowMode.TwoStep || draftStatus is PageVersionStatus.Approved);

        return new PageWorkflowState(
            page.Id,
            mode.ToString(),
            draftStatus.ToString(),
            pending,
            history,
            CanSubmit: mode is not WorkflowMode.None
                && draftStatus is not PageVersionStatus.InReview
                && authorization.HasPermission(CmsPermissions.ContentSubmit)
                && await acl.IsAllowedAsync(CmsPermissions.ContentEdit, page.Id, cancellationToken),
            CanDecide: pending is not null
                && canApprove
                && (mode is not WorkflowMode.TwoStep || pending.SubmittedByUserId != me),
            CanPublish: canPublish);
    }

    /// <summary>
    /// The projection, written once so the inbox, the history, and the review panel cannot describe
    /// the same row differently.
    /// </summary>
    /// <remarks>
    /// An expression rather than a method, because it is used inside <c>Select</c> and a method call
    /// there is something EF cannot translate — it would either fail at runtime or, worse, silently
    /// fetch every row and run in memory.
    /// <para>
    /// The submitter's name comes from a subquery rather than a navigation: submission is recorded
    /// by <c>CreatedBy</c>, which the fingerprint interceptor stamps and which carries no foreign
    /// key, so there is nothing to include. One correlated subquery over an indexed primary key is
    /// the cheaper of the two ways to get the name and the only one that does not add a column to
    /// every audited table in the schema.
    /// </para>
    /// </remarks>
    private Expression<Func<WorkflowTask, WorkflowTaskSummary>> Projection => task => new WorkflowTaskSummary(
        task.Id,
        task.PageId,
        task.Page.DraftVersion != null ? task.Page.DraftVersion.Title : "Untitled page",
        task.PageVersionId,
        task.PageVersion.VersionNumber,
        task.State.ToString(),
        task.AssignedToUserId,
        task.AssignedTo != null ? task.AssignedTo.UserName : null,
        task.DueDate,
        task.SubmissionNote,
        task.CreatedOn,
        task.CreatedBy,
        context.Users.Where(user => user.Id == task.CreatedBy).Select(user => user.UserName).FirstOrDefault(),
        task.DecidedOn,
        task.DecidedByUserId,
        task.DecidedBy != null ? task.DecidedBy.UserName : null,
        task.DecisionNote);

    private async Task CancelOpenAsync(int pageId, CancellationToken cancellationToken)
    {
        var open = await context.WorkflowTasks
            .Where(task => task.PageId == pageId && task.State == WorkflowState.Pending)
            .ToListAsync(cancellationToken);

        foreach (var task in open)
        {
            task.State = WorkflowState.Cancelled;
            task.DecidedOn = clock.GetUtcNow();
        }
    }

    private Task<WorkflowMode> ModeAsync(CancellationToken cancellationToken) =>
        context.SiteSettings
            .AsNoTracking()
            .Where(settings => settings.Id == SiteSettings.SingletonId)
            .Select(settings => settings.WorkflowMode)
            .FirstOrDefaultAsync(cancellationToken);

    private Task<Page?> LoadAsync(int pageId, bool tracked, CancellationToken cancellationToken)
    {
        var pages = tracked ? context.Pages : context.Pages.AsNoTracking();

        return pages
            .Include(page => page.DraftVersion)
            .FirstOrDefaultAsync(page => page.Id == pageId, cancellationToken);
    }

    /// <summary>Everyone who could act on a review request, for the unaddressed case.</summary>
    /// <remarks>
    /// Derived from the role-to-permission table rather than from a hard-coded "Approver", because
    /// section 21.1 gives the decision to administrators and developers as well and an inbox that
    /// left them out would be wrong on any site where the approver is the developer.
    /// </remarks>
    private async Task<IReadOnlyList<int>> ApproverIdsAsync(CancellationToken cancellationToken)
    {
        var roles = CmsRoles.ContentApprovers.Split(',');

        return await context.UserRoles
            .Where(assignment => context.Roles
                .Any(role => role.Id == assignment.RoleId && roles.Contains(role.Name)))
            .Select(assignment => assignment.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> CanDecideAsync(int userId, CancellationToken cancellationToken) =>
        (await ApproverIdsAsync(cancellationToken)).Contains(userId);

    private Task<string?> NameAsync(int userId, CancellationToken cancellationToken) =>
        context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.UserName)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<Dictionary<int, string>> PathsAsync(
        IReadOnlyList<int> pageIds,
        CancellationToken cancellationToken) =>
        await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => pageIds.Contains(page.Id))
            .Select(page => new { page.Id, page.Path })
            .ToDictionaryAsync(row => row.Id, row => row.Path, cancellationToken);

    private static string? Clean(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];

    private static CmsResult<PageWorkflowState> Forbidden(string message) =>
        CmsResult<PageWorkflowState>.Forbidden(message, WorkflowCodes.Forbidden);

    private static CmsResult<PageWorkflowState> NotFound(int pageId) =>
        CmsResult<PageWorkflowState>.NotFound($"No page has id {pageId}.", WorkflowCodes.NotFound);
}
