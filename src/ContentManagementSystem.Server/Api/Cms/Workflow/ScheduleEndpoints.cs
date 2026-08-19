using ContentManagementSystem.Core.Scheduling;
using ContentManagementSystem.Server.Api.Cms.Pages;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Server.Api.Cms.Workflow;

/// <summary>
/// <c>/api/cms/v1/pages/{id}/schedule</c> — publish and retirement times (task P7-16,
/// spec section 11.6).
/// </summary>
/// <remarks>
/// Instants cross this boundary as <see cref="DateTimeOffset"/>, so the offset the editor was
/// looking at travels with the moment they picked. The alternative — a local time plus a time zone
/// name resolved at the far end — has one wrong answer per year, on the morning the clocks go back,
/// when 01:30 happens twice.
/// </remarks>
public static class ScheduleEndpoints
{
    /// <summary>
    /// Maps the schedule endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapScheduleEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var schedule = group.MapGroup($"{PageEndpoints.Prefix}/{{pageId:int}}/schedule")
            .WithTags("Workflow");

        schedule.MapGet("/", GetAsync)
            .WithName("GetPageSchedule")
            .WithSummary("Reports what is scheduled for a page and how the last attempt ended.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        schedule.MapPost("/", SetAsync)
            .WithName("SetPageSchedule")
            .WithSummary("Sets, changes, or clears when a page publishes and retires.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> GetAsync(
        int pageId,
        ISchedulingService scheduling,
        CancellationToken cancellationToken) =>
        (await scheduling.GetAsync(pageId, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> SetAsync(
        int pageId,
        SetScheduleRequest request,
        ISchedulingService scheduling,
        CancellationToken cancellationToken) =>
        (await scheduling.SetAsync(pageId, request, cancellationToken)).ToHttpResult(Results.Ok);
}
