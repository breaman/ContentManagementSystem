using System.Security.Claims;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Authorization;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Extends the default claims principal factory to include the user's first name as a claim
/// so that the UI can display "Welcome, {FirstName}" without an extra database query, and the
/// permissions their roles grant so the backoffice can hide controls the server would refuse.
/// </summary>
public class CustomUserClaimsPrincipalFactory(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<User, Role>(userManager, roleManager, optionsAccessor)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        if (!string.IsNullOrWhiteSpace(user.FirstName))
        {
            identity.AddClaim(new Claim("FirstName", user.FirstName));
        }

        AddPermissionClaims(identity);

        return identity;
    }

    /// <summary>
    /// Stamps one <see cref="CmsClaimTypes.Permission"/> claim per permission the user's roles hold.
    /// </summary>
    /// <param name="identity">The identity being built, already carrying its role claims.</param>
    /// <remarks>
    /// Derived from the role claims the base factory has just added rather than from a second read
    /// of the role store, so a principal cannot end up holding a permission for a role it does not
    /// also claim. See <see cref="CmsClaimTypes.Permission"/> for why these are for display only.
    /// <para>
    /// Section ACLs are deliberately not represented here. They are per page, they change while a
    /// session is open, and a principal carrying a snapshot of them would be a cookie that grows
    /// with the content tree and goes stale the moment an administrator edits a rule.
    /// </para>
    /// </remarks>
    private void AddPermissionClaims(ClaimsIdentity identity)
    {
        var roles = identity.FindAll(Options.ClaimsIdentity.RoleClaimType)
            .Select(claim => claim.Value);

        foreach (var permission in CmsPermissionMap.PermissionsFor(roles))
        {
            identity.AddClaim(new Claim(CmsClaimTypes.Permission, permission));
        }
    }
}
