using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Telemetry;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Delivery;

/// <summary>
/// Registration and mapping for the public delivery path (task P3-13).
/// </summary>
public static class DeliveryEndpointRouteBuilderExtensions
{
    /// <summary>Route name of the catch-all, so a test can assert it is the endpoint that matched.</summary>
    public const string DeliveryRouteName = "cms-delivery";

    /// <summary>
    /// Registers the services the delivery endpoint resolves.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsDeliveryEndpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCmsDelivery();

        // Scoped, because it is constructed with the request's service provider and hands that to
        // the component renderer — a singleton here would give every request the root provider and
        // with it one shared database context.
        services.TryAddScoped<CmsPageRenderer>();

        // Also registered by AddCmsPages; TryAdd so the two cannot produce different meters.
        services.TryAddSingleton<CmsMetrics>();

        return services;
    }

    /// <summary>
    /// Maps the catch-all that serves content URLs.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// <strong>Call this last.</strong> Routing picks the most specific match rather than the first
    /// registered one, so a literal route such as <c>/health</c> beats <c>/{**slug}</c> whatever the
    /// order — but middleware-terminated paths such as the Blazor framework files are matched by
    /// order, and "registered last" is a rule that is cheap to keep and expensive to reason about
    /// once broken (risk R6). The <c>P3-15</c> tests assert the outcome rather than the ordering, so
    /// a future reshuffle fails on the behaviour that matters.
    /// <para>
    /// <c>CacheOutput</c> is deliberately not applied yet. Output caching is Phase 8, and a response
    /// cached before there is anything to evict it would make every publish look broken. The tags
    /// the policy will need are already collected and published on <c>HttpContext.Items</c> by the
    /// endpoint.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCmsDelivery(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/{**slug}", DeliveryEndpoint.HandleAsync)
            .AllowAnonymous()
            .WithName(DeliveryRouteName);

        return endpoints;
    }
}
