using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Routing;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Registration for the caching layer the delivery path reads through (tasks P8-08 to P8-10).
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the outbox, the invalidation queue, and the cached readers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call after <c>AddCmsDelivery()</c> and <c>AddCmsRouting()</c>: the two cached readers are
    /// decorators, and they replace the registrations those calls made rather than adding to them.
    /// The concrete inner types are registered alongside, because a decorator that resolved its own
    /// interface would resolve itself.
    /// <para>
    /// <c>ICacheInvalidator</c> is deliberately not registered here. The stores being evicted — the
    /// output cache middleware's and this process's <c>HybridCache</c> — belong to the host, which
    /// supplies the implementation.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsCaching(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHybridCache();

        services.TryAddScoped<ICacheInvalidationQueue, CacheInvalidationQueue>();
        services.TryAddScoped<OutboxRunner>();
        services.TryAddSingleton<OutboxState>();

        // The runner dispatches by message type, and this is the type it was built for. Registered
        // as an enumerable so the search indexer can add its own without either knowing about the
        // other (task P8-18).
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IOutboxMessageHandler, CacheInvalidationHandler>());

        // The readers the decorators wrap. Registered as their concrete types so that resolving
        // IPublishedContentService gives the cache and resolving PublishedContentService gives the
        // query — which is what preview and the invalidation-free paths want.
        services.TryAddScoped<PublishedContentService>();
        services.TryAddScoped<RouteResolver>();

        services.RemoveAll<IPublishedContentService>();
        services.AddScoped<IPublishedContentService, CachedPublishedContentService>();

        services.RemoveAll<IRouteResolver>();
        services.AddScoped<IRouteResolver, CachedRouteResolver>();

        return services;
    }
}
