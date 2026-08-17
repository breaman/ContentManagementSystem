using System.Globalization;
using System.Security.Claims;

using ContentManagementSystem.Server.Api.Cms.Media;
using ContentManagementSystem.Server.Api.Cms.Pages;
using ContentManagementSystem.Server.Api.Cms.Preview;
using ContentManagementSystem.Server.Api.Cms.Reusable;
using ContentManagementSystem.Server.Api.Cms.Routing;
using ContentManagementSystem.Server.Api.Cms.Structure;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Security;

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

        group.MapGet("/me", GetCurrentUser)
            .WithName("GetCurrentUser")
            .WithSummary("Reports the signed-in editor's own identity.");

        group.MapTemplateEndpoints();
        group.MapZoneEndpoints();
        group.MapBlockTypeEndpoints();
        group.MapCompositionEndpoints();

        group.MapPageEndpoints();
        group.MapPageLifecycleEndpoints();
        group.MapPageVersionEndpoints();
        group.MapPageLockEndpoints();

        group.MapReusableContentEndpoints();
        group.MapReferenceEndpoints();

        group.MapMediaEndpoints();

        group.MapRedirectEndpoints();

        group.MapPreviewTokenEndpoints();
        group.MapMarkupPreviewEndpoints();

        return group;
    }

    /// <summary>
    /// Answers who is signed in, by id as well as by name.
    /// </summary>
    /// <remarks>
    /// The backoffice runs in WebAssembly, where the serialized authentication state carries the name
    /// and role claims and nothing else — so the editor's own database id, which every screen that
    /// writes an <c>OwnerUserId</c> or filters "my work" needs, is not available there (task P6-17).
    /// <para>
    /// It reports the caller's own identity only, and needs no permission beyond the group's
    /// authentication floor: nobody learns anything here they did not arrive holding. It is
    /// deliberately not a user directory — listing other editors is Phase 7's, with Phase 7's rules.
    /// </para>
    /// </remarks>
    private static IResult GetCurrentUser(HttpContext httpContext)
    {
        var principal = httpContext.User;

        if (!int.TryParse(
                principal.FindFirstValue(ClaimTypes.NameIdentifier),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var userId))
        {
            // Authenticated by a scheme that issued no numeric subject. There is nothing to report
            // and nothing wrong, which is what 204 means.
            return Results.NoContent();
        }

        return Results.Ok(new CurrentUser(
            userId,
            principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Email) ?? $"user {userId}"));
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
