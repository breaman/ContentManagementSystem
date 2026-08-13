using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Authorization;

/// <summary>
/// Answers permission questions about the current HTTP request's caller.
/// </summary>
/// <param name="accessor">Access to the current request, and through it the caller's principal.</param>
/// <remarks>
/// The web-side half of <see cref="ICmsAuthorization"/>. Roles come from the cookie principal the
/// Identity stack already builds, so there is no second notion of who someone is.
/// <para>
/// No request — a hosted service, a CLI verb, a background job — means no permissions. That is the
/// safe default and it is deliberately inconvenient: a background job that needs to write content
/// must be given an explicit identity rather than inheriting one by accident.
/// </para>
/// </remarks>
public sealed class HttpCmsAuthorization(IHttpContextAccessor accessor) : ICmsAuthorization
{
    /// <inheritdoc />
    public bool HasPermission(string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        if (accessor.HttpContext?.User is not { Identity.IsAuthenticated: true } user) return false;

        if (!CmsPermissionMap.RolesByPermission.TryGetValue(permission, out var roles))
        {
            // An unmapped permission is a coding error, not a grant. Refusing is the only answer
            // that fails in the direction that does not leak.
            return false;
        }

        for (var i = 0; i < roles.Count; i++)
        {
            if (user.IsInRole(roles[i])) return true;
        }

        return false;
    }
}
