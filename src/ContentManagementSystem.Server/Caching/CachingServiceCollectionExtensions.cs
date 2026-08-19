using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.ServiceDefaults;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Caching;

/// <summary>
/// Registration for output caching and cache invalidation (tasks P8-06, P8-10, P8-11).
/// </summary>
public static class CachingServiceCollectionExtensions
{
    /// <summary>Name of the policy the delivery endpoint is cached under.</summary>
    public const string PagePolicyName = "cms-page";

    /// <summary>
    /// Registers the output cache, the CMS page policy, and the invalidator that evicts both caches.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Read for the optional Redis connection string.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// <strong>Redis is opt-in and is the only supported way to run more than one instance</strong>
    /// (spec section 16.3). Without it each instance has its own in-memory output cache, and a
    /// publish evicts the cache of whichever node dispatched it — which is correct for the default
    /// single-instance deployment and stale content for any other. With it the output cache is
    /// shared, so one eviction serves every node.
    /// <para>
    /// <c>IDistributedCache</c> is deliberately not registered. It is not used for output caching —
    /// it has no atomic operations for tag eviction — and registering one would silently give
    /// <c>HybridCache</c> a second level whose serialization requirements nothing here has been
    /// designed for.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsOutputCache(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var redis = configuration.GetConnectionString(Constants.OutputCacheConnectionString);

        if (!string.IsNullOrWhiteSpace(redis))
        {
            services.AddStackExchangeRedisOutputCache(options => options.Configuration = redis);
        }

        services.AddOutputCache(options => options.AddPolicy(PagePolicyName, ResolvePolicy.Instance));

        services.TryAddSingleton<ICacheInvalidator, OutputCacheInvalidator>();
        services.TryAddSingleton<CmsPageCachePolicy>();

        return services;
    }

    /// <summary>
    /// Bridges the named policy to the one in the container.
    /// </summary>
    /// <remarks>
    /// <c>AddPolicy</c> takes an instance, and <see cref="CmsPageCachePolicy"/> needs the options and
    /// the metrics from dependency injection. This resolves it per request from the request's own
    /// provider, which is the seam the output-cache API leaves for that.
    /// </remarks>
    private sealed class ResolvePolicy : IOutputCachePolicy
    {
        /// <summary>The single instance registered as the named policy.</summary>
        public static ResolvePolicy Instance { get; } = new();

        ValueTask IOutputCachePolicy.CacheRequestAsync(
            OutputCacheContext context,
            CancellationToken cancellationToken) =>
            Policy(context).CacheRequestAsync(context, cancellationToken);

        ValueTask IOutputCachePolicy.ServeFromCacheAsync(
            OutputCacheContext context,
            CancellationToken cancellationToken) =>
            Policy(context).ServeFromCacheAsync(context, cancellationToken);

        ValueTask IOutputCachePolicy.ServeResponseAsync(
            OutputCacheContext context,
            CancellationToken cancellationToken) =>
            Policy(context).ServeResponseAsync(context, cancellationToken);

        private static IOutputCachePolicy Policy(OutputCacheContext context) =>
            context.HttpContext.RequestServices.GetRequiredService<CmsPageCachePolicy>();
    }
}
