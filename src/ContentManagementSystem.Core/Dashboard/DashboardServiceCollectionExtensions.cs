using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Dashboard;

/// <summary>
/// Registration helper for the backoffice landing screen (tasks P6-24 to P6-27).
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Registers the dashboard service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Scoped, because it holds a database context. It reads across pages, media, references, the
    /// audit log, and the not-found log without owning any of them — which is why it is a service of
    /// its own rather than a method on one of the five.
    /// </remarks>
    public static IServiceCollection AddCmsDashboard(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IDashboardService, DashboardService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
