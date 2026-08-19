using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Auditing;

/// <summary>Registration helper for the audit log viewer (task P7-20).</summary>
public static class AuditingServiceCollectionExtensions
{
    /// <summary>Registers the read-only audit query service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsAuditing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IAuditQueryService, AuditQueryService>();

        // The nightly sweep of task P9-25. Scoped, because it holds a database context; the loop that
        // calls it takes a scope per pass.
        services.TryAddScoped<IAuditRetentionService, AuditRetentionService>();

        return services;
    }
}
