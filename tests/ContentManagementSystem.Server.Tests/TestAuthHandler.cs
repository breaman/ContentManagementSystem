using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Tests;

/// <summary>
/// Signs a request in as whoever its headers say, so an endpoint's authorization can be tested
/// without driving the Identity login flow.
/// </summary>
/// <remarks>
/// What this replaces is only the part that proves <em>who</em> the caller is. The roles it issues
/// are ordinary role claims, so the policies, the permission map, and the service-layer checks under
/// test are the real ones — the assertions still fail if any of them is wrong.
/// <para>
/// A request with no roles header is anonymous rather than authenticated-with-nothing. Both are
/// worth testing and they produce different status codes (401 against 403), which would be
/// impossible to tell apart if this handler always signed someone in.
/// </para>
/// </remarks>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>Name of the scheme this handler registers under.</summary>
    public const string SchemeName = "Test";

    /// <summary>Header carrying the caller's roles, comma-separated.</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>Header carrying the caller's user id, which fingerprinting stamps onto rows.</summary>
    public const string UserIdHeader = "X-Test-User-Id";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(RolesHeader, out var roles))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = Request.Headers.TryGetValue(UserIdHeader, out var suppliedId)
            ? suppliedId.ToString()
            : "1";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, $"test-user-{userId}"),
        };

        foreach (var role in roles.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries |
                                                        StringSplitOptions.TrimEntries))
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
