using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Appearance;

/// <summary>Registration for the administrator-authored site stylesheet (task P10-03).</summary>
public static class AppearanceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the stylesheet validator, its editing service, and the anonymous reader delivery
    /// uses.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration the stylesheet's limits bind from.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsAppearance(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SiteStylesheetOptions>(
            configuration.GetSection(SiteStylesheetOptions.SectionName));

        // Singleton: it holds no state beyond its options and is called on every keystroke the
        // editor debounces, as well as on every save and publish.
        services.TryAddSingleton<ICssValidator, CssValidator>();

        services.TryAddScoped<ISiteStylesheetService, SiteStylesheetService>();
        services.TryAddScoped<IPublishedStylesheetReader, PublishedStylesheetReader>();

        return services;
    }
}
