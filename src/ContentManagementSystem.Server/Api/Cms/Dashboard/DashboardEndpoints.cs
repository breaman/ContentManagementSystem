using ContentManagementSystem.Core.Dashboard;
using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Dashboard;

/// <summary>
/// <c>/api/cms/v1/dashboard</c> — the backoffice landing screen (spec section 14.9).
/// </summary>
/// <remarks>
/// Two routes: every tile trimmed, and one tile at length. The second is what a tile's "show all"
/// link opens, and it runs the same queries with a larger limit — which is what makes the tile and
/// the list it links to agree rather than merely resemble each other
/// (acceptance criterion P6 #8).
/// </remarks>
public static class DashboardEndpoints
{
    /// <summary>Route prefix the dashboard endpoints hang off, relative to the versioned group.</summary>
    internal const string Prefix = "/dashboard";

    /// <summary>
    /// Maps the dashboard endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var dashboard = group.MapGroup(Prefix).WithTags("Dashboard");

        dashboard.MapGet("/", GetAsync)
            .WithName("GetDashboard")
            .WithSummary("Reads every dashboard tile, trimmed for the landing screen.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        dashboard.MapGet("/{tile}", GetTileAsync)
            .WithName("GetDashboardTile")
            .WithSummary("Reads one dashboard tile at length, for the list its link opens.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        return group;
    }

    private static async Task<IResult> GetAsync(
        IDashboardService dashboard,
        CancellationToken cancellationToken,
        int limit = 5) =>
        (await dashboard.GetAsync(limit, cancellationToken)).ToHttpResult(Results.Ok);

    /// <remarks>
    /// The tile is bound as the enum, so an unrecognised name is a <c>400</c> from the framework
    /// rather than an empty screen. That is the same rule the page list's status filter follows: a
    /// filter the server silently drops answers a question nobody asked.
    /// </remarks>
    private static async Task<IResult> GetTileAsync(
        DashboardTile tile,
        IDashboardService dashboard,
        CancellationToken cancellationToken,
        int limit = 100) =>
        (await dashboard.GetTileAsync(tile, limit, cancellationToken)).ToHttpResult(Results.Ok);
}
