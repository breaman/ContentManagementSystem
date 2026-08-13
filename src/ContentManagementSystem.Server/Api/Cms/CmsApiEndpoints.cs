using ContentManagementSystem.Server.Api.Cms.Structure;

using Microsoft.AspNetCore.Antiforgery;

namespace ContentManagementSystem.Server.Api.Cms;

/// <summary>
/// The management API the Blazor WebAssembly backoffice talks to (spec section 22).
/// </summary>
/// <remarks>
/// Everything hangs off one versioned group. The version is a URL segment rather than a header
/// because the backoffice is shipped from the same origin as the API and both are deployed
/// together — a visible <c>/v1</c> makes the one case that matters, an old cached client calling a
/// newer server, obvious in a log rather than invisible in a header.
/// </remarks>
public static class CmsApiEndpoints
{
    /// <summary>
    /// Prefix every API path shares, used to keep the site's HTML error pages away from them.
    /// </summary>
    public const string ApiPathPrefix = "/api";

    /// <summary>Base path of the current API version.</summary>
    public const string BasePath = $"{ApiPathPrefix}/cms/v1";

    /// <summary>
    /// Maps every CMS management endpoint.
    /// </summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <returns>The versioned group, so a caller can extend it.</returns>
    public static RouteGroupBuilder MapCmsApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var group = endpoints.MapGroup(BasePath);

        // Every endpoint in the group requires a signed-in user. The per-permission policies below
        // narrow that further; this is the floor, so a new endpoint added without a policy is
        // private rather than public.
        group.RequireAuthorization();

        group.MapGet("/antiforgery-token", GetAntiforgeryToken)
            .WithName("GetAntiforgeryToken")
            .WithSummary("Issues the antiforgery token pair the write endpoints require.");

        group.MapTemplateEndpoints();

        return group;
    }

    /// <summary>
    /// Issues an antiforgery token pair: the cookie half as a <c>Set-Cookie</c>, the request half in
    /// the body for the client to echo back in a header.
    /// </summary>
    /// <remarks>
    /// Authenticated, like the rest of the group. Tokens are bound to the signed-in user, so an
    /// anonymous caller could only ever fetch one it cannot use.
    /// </remarks>
    private static IResult GetAntiforgeryToken(HttpContext httpContext, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(httpContext);

        return Results.Ok(new AntiforgeryTokenResponse(
            tokens.HeaderName ?? CmsAntiforgeryDefaults.HeaderName,
            tokens.RequestToken ?? string.Empty));
    }
}

/// <summary>
/// Body of <c>GET /api/cms/v1/antiforgery-token</c>.
/// </summary>
/// <param name="HeaderName">Header the request token must be sent in.</param>
/// <param name="RequestToken">The request token to echo back on writes.</param>
/// <remarks>
/// The header name is returned rather than hard-coded in the client, so changing it is a server-side
/// configuration change and not a coordinated deployment.
/// </remarks>
public sealed record AntiforgeryTokenResponse(string HeaderName, string RequestToken);

/// <summary>Antiforgery settings shared by the server configuration and its clients.</summary>
public static class CmsAntiforgeryDefaults
{
    /// <summary>Header the management API reads the request token from.</summary>
    public const string HeaderName = "X-CSRF-TOKEN";
}
