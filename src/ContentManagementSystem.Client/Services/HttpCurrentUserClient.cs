using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// The WebAssembly half of <see cref="ICurrentUserClient"/>, over the management API (task P6-17).
/// </summary>
/// <param name="http">Client bound to the application's own origin.</param>
/// <remarks>
/// The answer is fetched once and kept for the client's lifetime, which is the browsing session:
/// who is signed in cannot change without a sign-out, and a sign-out replaces the whole application.
/// </remarks>
public sealed class HttpCurrentUserClient(HttpClient http) : ICurrentUserClient
{
    private CurrentUser? _user;

    /// <inheritdoc />
    public async Task<CurrentUser?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_user is not null) return _user;

        var response = await http.GetAsync("api/cms/v1/me", cancellationToken);

        // 204 is an authenticated caller whose scheme issued no numeric subject, and 401 is nobody
        // at all. Neither is a fault, and both mean the same thing to a screen: no id to write.
        if (response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.Unauthorized)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        return _user = await response.Content.ReadFromJsonAsync<CurrentUser>(cancellationToken);
    }
}
