using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Core.Scheduling;

/// <summary>
/// Sets and clears the moment a page publishes or retires (tasks P7-13 to P7-16,
/// spec section 11.6).
/// </summary>
/// <remarks>
/// Two things are written for one schedule and they say different things.
/// <see cref="Data.Models.Cms.PageVersion.PublishOn"/> is what the editor asked for and is what the
/// editor's screens read; the <c>ScheduledJob</c> row is the instruction the poller claims. Keeping
/// both is what lets a failed job say <em>why</em> while the request that produced it is still
/// visible beside it.
/// </remarks>
public interface ISchedulingService
{
    /// <summary>Reports what is scheduled for a page.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The schedule, the site's time zone, and the last attempt's outcome.</returns>
    Task<CmsResult<PageScheduleState>> GetAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>Sets, changes, or clears a page's schedule.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="request">The instants wanted, either of which may be null to cancel.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The schedule as it now stands.</returns>
    /// <remarks>
    /// Rescheduling replaces rather than stacks: the outstanding job of that kind is cancelled and a
    /// new one written, under a filtered unique index that refuses two outstanding jobs of one kind
    /// per page. Two pending publishes for one page is the shape of a double publish.
    /// </remarks>
    Task<CmsResult<PageScheduleState>> SetAsync(
        int pageId,
        SetScheduleRequest request,
        CancellationToken cancellationToken = default);
}
