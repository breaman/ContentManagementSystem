using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Navigation;

/// <summary>Registration for navigation (tasks P8-15, P8-16).</summary>
public static class NavigationServiceCollectionExtensions
{
    /// <summary>Registers the navigation reader.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsNavigation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<INavigationService, NavigationService>();
        services.TryAddScoped<INavigationMenuService, NavigationMenuService>();

        return services;
    }
}
