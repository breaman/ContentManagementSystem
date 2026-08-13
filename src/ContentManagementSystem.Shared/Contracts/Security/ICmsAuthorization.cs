namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// What the caller of the current request is permitted to do.
/// </summary>
/// <remarks>
/// Domain services depend on this rather than on <c>ClaimsPrincipal</c> or ASP.NET Core's
/// <c>IAuthorizationService</c>, which keeps <c>Core</c> free of a web dependency while still
/// letting authorization be enforced where <c>CONTRIBUTING.md</c> requires it — in the service
/// layer, not only at the endpoint. An endpoint policy is a fast rejection at the door; this is the
/// check that still runs when a service is called from a CLI verb, a hosted job, or a second
/// endpoint someone forgot to decorate.
/// <para>
/// Phase 7 adds the section-ACL overload — a permission asked about a particular page — to this same
/// seam (spec section 21.2).
/// </para>
/// </remarks>
public interface ICmsAuthorization
{
    /// <summary>Whether the caller holds a global permission.</summary>
    /// <param name="permission">One of the <see cref="CmsPermissions"/> constants.</param>
    /// <returns><see langword="true"/> when the caller may proceed.</returns>
    bool HasPermission(string permission);
}
