using ContentManagementSystem.Shared.Contracts.Dashboard;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the backoffice landing screen needs from the server (tasks P6-24 to P6-27).
/// </summary>
/// <remarks>
/// Implemented twice, as every client in this namespace is: over <c>HttpClient</c> in the WebAssembly
/// backoffice, and directly over the dashboard service on the server so the landing screen
/// pre-renders with its tiles filled in. A dashboard that arrives as four spinners is a dashboard
/// nobody reads.
/// <para>
/// Reads only, and therefore bare values rather than a result type: a failure here is an empty tile
/// or a transport fault, never a rule somebody needs read back to them.
/// </para>
/// </remarks>
public interface IDashboardClient
{
    /// <summary>Reads every tile, trimmed for the landing screen.</summary>
    /// <param name="limit">How many rows each list shows.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<DashboardContent?> GetAsync(int limit = 5, CancellationToken cancellationToken = default);

    /// <summary>Reads one tile at length, for the list its link opens.</summary>
    /// <param name="tile">Which tile.</param>
    /// <param name="limit">How many rows each of its lists shows.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<DashboardTileContent?> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
