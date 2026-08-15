using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Routing;

/// <inheritdoc cref="IRouteResolver" />
/// <param name="context">The application database context.</param>
/// <param name="redirects">Consulted only after the routes have found nothing.</param>
public sealed class RouteResolver(ApplicationDbContext context, IRedirectService redirects) : IRouteResolver
{
    /// <inheritdoc />
    public async Task<RouteResolution> ResolveAsync(
        string? url,
        CancellationToken cancellationToken = default)
    {
        var canonical = SiteUrls.Normalize(url);
        var hash = SiteUrls.Hash(canonical);

        var route = await context.PageRoutes
            .AsNoTracking()
            .Where(candidate => candidate.UrlHash == hash && candidate.IsPublished)
            .Select(candidate => new { candidate.PageId, candidate.Url })
            .FirstOrDefaultAsync(cancellationToken);

        if (route is not null)
        {
            // The same content must not be reachable at two spellings. A request that arrived as
            // '/About/' resolved to the right page here, and is sent on to the canonical URL rather
            // than served — which is what keeps the canonical tag, the sitemap, and the analytics
            // report describing one address (spec section 10.3).
            var needsCanonicalRedirect = !string.Equals(
                url?.TrimEnd() ?? string.Empty,
                route.Url,
                StringComparison.Ordinal);

            return new RouteResolution(
                RouteResolutionKind.Page,
                route.PageId,
                CanonicalUrl: needsCanonicalRedirect ? route.Url : null);
        }

        var redirect = await redirects.ResolveAsync(canonical, cancellationToken);

        return redirect is null
            ? RouteResolution.NotFound
            : new RouteResolution(
                RouteResolutionKind.Redirect,
                TargetUrl: redirect.TargetUrl,
                StatusCode: redirect.StatusCode,
                RedirectId: redirect.RedirectId);
    }
}
