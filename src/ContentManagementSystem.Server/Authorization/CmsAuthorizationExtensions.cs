using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Authorization;

/// <summary>
/// Registers one authorization policy per CMS permission.
/// </summary>
/// <remarks>
/// Policies are named after the permission they enforce, so an endpoint reads
/// <c>RequireAuthorization(CmsPermissions.StructureEdit)</c> and the service behind it asks
/// <c>HasPermission(CmsPermissions.StructureEdit)</c> — the same string in both places.
/// </remarks>
public static class CmsAuthorizationExtensions
{
    /// <summary>
    /// Adds the CMS permission policies and the request-scoped permission evaluator.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<ICmsAuthorization, HttpCmsAuthorization>();

        var authorization = services.AddAuthorizationBuilder();

        foreach (var (permission, roles) in CmsPermissionMap.RolesByPermission)
        {
            authorization.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(roles));
        }

        return services;
    }
}
