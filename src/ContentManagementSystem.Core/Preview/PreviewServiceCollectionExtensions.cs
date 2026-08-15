using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Preview;

/// <summary>
/// Registration helpers for the preview path (tasks P3-16 to P3-19).
/// </summary>
public static class PreviewServiceCollectionExtensions
{
    /// <summary>
    /// Registers the version loader and the shareable-link services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call alongside <c>AddCmsDelivery()</c>. Preview renders through the identical pipeline the
    /// public site uses (spec section 12.1) and differs only in which version it loads, so a host
    /// carrying one without the other is a host where preview fidelity has quietly stopped being
    /// structural.
    /// </remarks>
    public static IServiceCollection AddCmsPreview(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IPreviewContentService, PreviewContentService>();
        services.TryAddScoped<IPreviewTokenService, PreviewTokenService>();

        // Shared with the page and routing services; TryAdd means whichever call runs first wins and
        // a token's expiry is judged against the same clock that stamped it.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
