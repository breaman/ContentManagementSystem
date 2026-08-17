using System.Globalization;
using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Dashboard;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="IDashboardClient"/>, over the management API
/// (tasks P6-24 to P6-27).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// Nothing is cached here, unlike the current-user client beside it. The dashboard's whole subject
/// is what has changed, and a landing screen answering from a copy it took when the tab was opened
/// would tell an editor their overdue review is still overdue after they had just done it.
/// </remarks>
public sealed class HttpDashboardClient(HttpClient http) : IDashboardClient
{
    private const string Base = "api/cms/v1/dashboard";

    /// <inheritdoc />
    public Task<DashboardContent?> GetAsync(int limit = 5, CancellationToken cancellationToken = default) =>
        ReadAsync<DashboardContent>(
            $"{Base}?limit={limit.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);

    /// <inheritdoc />
    public Task<DashboardTileContent?> GetTileAsync(
        DashboardTile tile,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        ReadAsync<DashboardTileContent>(
            $"{Base}/{tile}?limit={limit.ToString(CultureInfo.InvariantCulture)}",
            cancellationToken);

    /// <summary>Reads a tile, treating "you may not" as an empty screen rather than a fault.</summary>
    /// <remarks>
    /// A viewer who cannot read content has no dashboard, which is a state to draw rather than an
    /// exception to throw at somebody who did nothing but open the front page.
    /// </remarks>
    private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        var response = await http.GetAsync(path, cancellationToken);

        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<T>(cancellationToken);
    }
}
