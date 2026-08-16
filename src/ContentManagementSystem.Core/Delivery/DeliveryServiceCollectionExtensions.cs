using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// Registration helpers for the read-only public delivery path.
/// </summary>
public static class DeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services the public site reads content through.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call alongside <c>AddCmsContent()</c>, which supplies the schema catalog, and
    /// <c>AddCmsRouting()</c>, which supplies the resolver that turns a request URL into the page id
    /// this takes.
    /// <para>
    /// Scoped, because it holds a database context. It is registered separately from
    /// <c>AddCmsPages()</c> so that the read path and the write path can be reasoned about — and, if
    /// it ever comes to it, hosted — apart.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IPublishedContentService, PublishedContentService>();
        services.TryAddScoped<IReusableContentResolver, ReusableContentResolver>();

        return services;
    }
}
