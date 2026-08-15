using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Routing;

/// <summary>
/// Registration helpers for the routing services.
/// </summary>
public static class RoutingServiceCollectionExtensions
{
    /// <summary>
    /// Registers URL materialization, redirects, and route resolution.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call alongside <c>AddCmsPages()</c>. The page and publishing services rebuild routes as a
    /// side effect of every URL-affecting change, so a container carrying one without the other
    /// fails to resolve rather than quietly serving pages that have no URLs.
    /// </remarks>
    public static IServiceCollection AddCmsRouting(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IRedirectService, RedirectService>();
        services.TryAddScoped<IUrlService, UrlService>();
        services.TryAddScoped<IRouteResolver, RouteResolver>();
        services.TryAddScoped<ILinkResolver, LinkResolver>();
        services.TryAddScoped<INotFoundLogService, NotFoundLogService>();

        // Shared with the page services; TryAdd means whichever call runs first wins and the two
        // cannot end up with different clocks.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
