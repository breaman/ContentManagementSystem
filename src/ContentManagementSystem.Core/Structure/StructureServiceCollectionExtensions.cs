using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Registration helpers for the structure services behind the content-model admin.
/// </summary>
public static class StructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the template, zone, and block-type services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Scoped, because they hold the request's <see cref="Data.Models.ApplicationDbContext"/>. Each
    /// one also resolves an <see cref="Shared.Contracts.Security.ICmsAuthorization"/>, which the
    /// hosting layer supplies — <c>Core</c> has no way to see who is calling, and a default that
    /// permitted everything would make the service-layer checks decorative.
    /// </remarks>
    public static IServiceCollection AddCmsStructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ITemplateService, TemplateService>();

        return services;
    }
}
