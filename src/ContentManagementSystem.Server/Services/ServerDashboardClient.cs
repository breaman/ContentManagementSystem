using ContentManagementSystem.Core.Dashboard;
using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IDashboardClient"/>, over the dashboard service directly
/// (tasks P6-24 to P6-27).
/// </summary>
/// <param name="dashboard">The tiles.</param>
/// <remarks>
/// Used while pre-rendering, so the landing screen arrives with its tiles filled in rather than as
/// four spinners the editor watches. The service authorizes the caller itself, against the same
/// request principal the API would have seen, so the shortcut changes nothing about who sees what.
/// </remarks>
public sealed class ServerDashboardClient(IDashboardService dashboard) : IDashboardClient
{
    /// <inheritdoc />
    public async Task<DashboardContent?> GetAsync(
        int limit = 5,
        CancellationToken cancellationToken = default) =>
        (await dashboard.GetAsync(limit, cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<DashboardTileContent?> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        (await dashboard.GetTileAsync(tile, limit, cancellationToken)).Value;
}
