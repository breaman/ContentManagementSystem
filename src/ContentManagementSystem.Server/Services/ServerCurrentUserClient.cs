using System.Globalization;
using System.Security.Claims;

using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="ICurrentUserClient"/>, over the request principal (task P6-17).
/// </summary>
/// <param name="accessor">The current request, which is where the principal lives.</param>
/// <remarks>
/// Used while pre-rendering, so the properties panel arrives already knowing whether the signed-in
/// editor owns the page rather than drawing "unassigned" and correcting itself a moment later. It
/// reads the same claim the API endpoint reads, from the same principal.
/// </remarks>
public sealed class ServerCurrentUserClient(IHttpContextAccessor accessor) : ICurrentUserClient
{
    /// <inheritdoc />
    public Task<CurrentUser?> GetAsync(CancellationToken cancellationToken = default)
    {
        var principal = accessor.HttpContext?.User;

        if (principal?.Identity?.IsAuthenticated is not true)
        {
            return Task.FromResult<CurrentUser?>(null);
        }

        if (!int.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var userId))
        {
            return Task.FromResult<CurrentUser?>(null);
        }

        return Task.FromResult<CurrentUser?>(new CurrentUser(
            userId,
            principal.Identity.Name ?? principal.FindFirstValue(ClaimTypes.Email) ?? $"user {userId}"));
    }
}
