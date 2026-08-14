using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Registration helpers for the page services.
/// </summary>
/// <remarks>
/// Separate from <c>AddCmsContent()</c>, which registers the payload engine. These services hold a
/// database context and are therefore scoped, while the payload engine is stateless and singleton —
/// mixing the two lifetimes behind one call is how a singleton ends up capturing a request's
/// context.
/// </remarks>
public static class PageServiceCollectionExtensions
{
    /// <summary>
    /// Registers the services that own the content tree.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsPages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IPageTreeService, PageTreeService>();
        services.TryAddScoped<IPageService, PageService>();

        return services;
    }
}
