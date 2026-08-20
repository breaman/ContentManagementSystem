using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>
/// Registration for the load-test dataset seeder (task P9-12).
/// </summary>
public static class LoadTestingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the seeder the <c>cms seed</c> verbs run.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Registering it does nothing on its own: it is reachable only from the command line, never
    /// from an endpoint, which is what keeps a tool that writes half a million rows out of reach of
    /// a request. It needs the media store, so call it after <c>AddCmsMedia()</c>.
    /// </remarks>
    public static IServiceCollection AddCmsLoadTestSeeding(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ILoadTestSeeder, LoadTestSeeder>();

        return services;
    }
}
