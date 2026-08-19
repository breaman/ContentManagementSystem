using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Scheduling;

/// <inheritdoc cref="ISchedulingService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (spec section 21.2).</param>
/// <param name="users">Who the caller is, which is whose identity the job later runs under.</param>
/// <param name="clock">Source of the current time, so "in the future" is testable.</param>
/// <param name="logger">Log for every schedule set and cleared.</param>
public sealed class SchedulingService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IAclService acl,
    IUserService users,
    TimeProvider clock,
    ILogger<SchedulingService> logger) : ISchedulingService
{
    /// <inheritdoc />
    public async Task<CmsResult<PageScheduleState>> GetAsync(
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

        return page?.DraftVersion is null
            ? NotFound(pageId)
            : CmsResult<PageScheduleState>.Success(await ProjectAsync(page, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<CmsResult<PageScheduleState>> SetAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Scheduling is publishing with a delay, so it is the publish permission that governs it —
        // section 21.1 gives the two to exactly the same roles for exactly this reason.
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden("Publishing is not permitted.");
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentPublish, pageId, cancellationToken))
        {
            return Forbidden($"Publishing page {pageId} is not permitted.");
        }

        var page = await LoadAsync(pageId, tracked: true, cancellationToken);

        if (page?.DraftVersion is null) return NotFound(pageId);

        var now = clock.GetUtcNow();

        if (request.PublishOn is { } publishOn && publishOn <= now)
        {
            return CmsResult<PageScheduleState>.Invalid(
                PageCodes.OutOfRange,
                "A scheduled publish has to be in the future. To publish now, publish now.",
                nameof(SetScheduleRequest.PublishOn));
        }

        if (request.UnpublishOn is { } unpublishOn && unpublishOn <= now)
        {
            return CmsResult<PageScheduleState>.Invalid(
                PageCodes.OutOfRange,
                "A scheduled retirement has to be in the future. To retire the page now, unpublish it.",
                nameof(SetScheduleRequest.UnpublishOn));
        }

        if (request is { PublishOn: { } from, UnpublishOn: { } until } && until <= from)
        {
            return CmsResult<PageScheduleState>.Invalid(
                PageCodes.OutOfRange,
                "The page would be retired before it was published.",
                nameof(SetScheduleRequest.UnpublishOn));
        }

        var draft = page.DraftVersion;

        draft.PublishOn = request.PublishOn;
        draft.UnpublishOn = request.UnpublishOn;

        await ReplaceJobAsync(page, ScheduledJobKind.Publish, request.PublishOn, draft.Id, cancellationToken);
        await ReplaceJobAsync(page, ScheduledJobKind.Unpublish, request.UnpublishOn, null, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Page {PageId} scheduled by user {UserId}: publish {PublishOn}, unpublish {UnpublishOn}.",
            pageId,
            users.UserId,
            request.PublishOn?.ToString("O") ?? "never",
            request.UnpublishOn?.ToString("O") ?? "never");

        return CmsResult<PageScheduleState>.Success(await ProjectAsync(page, cancellationToken));
    }

    /// <summary>
    /// Cancels the outstanding job of one kind and, when an instant was given, writes its successor.
    /// </summary>
    /// <remarks>
    /// Cancel-then-insert rather than update-in-place. An outstanding job may already have been
    /// claimed by a poller on another instance, and mutating a row somebody else is executing is how
    /// two instances end up disagreeing about what they are doing; cancelling it means the claim
    /// finds a job that is no longer pending and stops.
    /// </remarks>
    private async Task ReplaceJobAsync(
        Page page,
        ScheduledJobKind kind,
        DateTimeOffset? runOn,
        int? versionId,
        CancellationToken cancellationToken)
    {
        var outstanding = await context.ScheduledJobs
            .Where(job => job.PageId == page.Id
                && job.Kind == kind
                && (job.State == ScheduledJobState.Pending || job.State == ScheduledJobState.Claimed))
            .ToListAsync(cancellationToken);

        foreach (var job in outstanding)
        {
            job.State = ScheduledJobState.Cancelled;
            job.CompletedOn = clock.GetUtcNow();
        }

        if (runOn is null) return;

        context.ScheduledJobs.Add(new ScheduledJob
        {
            PageId = page.Id,
            PageVersionId = versionId,
            Kind = kind,
            RunOn = runOn.Value,
            State = ScheduledJobState.Pending,
            OwnerUserId = users.UserId,
        });
    }

    private async Task<PageScheduleState> ProjectAsync(Page page, CancellationToken cancellationToken)
    {
        var timeZone = await context.SiteSettings
            .AsNoTracking()
            .Where(settings => settings.Id == SiteSettings.SingletonId)
            .Select(settings => settings.TimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

        var jobs = await context.ScheduledJobs
            .AsNoTracking()
            .Where(job => job.PageId == page.Id)
            .OrderByDescending(job => job.Id)
            .Take(10)
            .Select(job => new { job.Kind, job.State, job.FailureReason })
            .ToListAsync(cancellationToken);

        var publish = jobs.FirstOrDefault(job => job.Kind == ScheduledJobKind.Publish);
        var unpublish = jobs.FirstOrDefault(job => job.Kind == ScheduledJobKind.Unpublish);

        return new PageScheduleState(
            page.Id,
            page.DraftVersion?.PublishOn,
            page.DraftVersion?.UnpublishOn,
            timeZone,
            publish?.State.ToString(),
            unpublish?.State.ToString(),
            publish?.FailureReason ?? unpublish?.FailureReason);
    }

    private Task<Page?> LoadAsync(int pageId, bool tracked, CancellationToken cancellationToken)
    {
        var pages = tracked ? context.Pages : context.Pages.AsNoTracking();

        return pages
            .Include(page => page.DraftVersion)
            .FirstOrDefaultAsync(page => page.Id == pageId, cancellationToken);
    }

    private static CmsResult<PageScheduleState> Forbidden(string message) =>
        CmsResult<PageScheduleState>.Forbidden(message, PageCodes.Forbidden);

    private static CmsResult<PageScheduleState> NotFound(int pageId) =>
        CmsResult<PageScheduleState>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
}
