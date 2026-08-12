using System.Security.Claims;

using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Extends the default claims principal factory to include the user's first name as a claim
/// so that the UI can display "Welcome, {FirstName}" without an extra database query.
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

        return identity;
    }
}
