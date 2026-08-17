using ContentManagementSystem.Core.Dashboard;
using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IDashboardClient"/>, over the dashboard service directly
/// (tasks P6-24 to P6-27).
/// </summary>
/// <param name="dashboard">The tiles.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used while pre-rendering, so the landing screen arrives with its tiles filled in rather than as
/// four spinners the editor watches. The service authorizes the caller itself, against the same
/// request principal the API would have seen, so the shortcut changes nothing about who sees what.
/// </remarks>
public sealed class ServerDashboardClient(IDashboardService dashboard, PrerenderGate gate) : IDashboardClient
{
    /// <inheritdoc />
    public async Task<DashboardContent?> GetAsync(
        int limit = 5,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => dashboard.GetAsync(limit, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<DashboardTileContent?> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => dashboard.GetTileAsync(tile, limit, token), cancellationToken)).Value;
}
