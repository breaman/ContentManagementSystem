using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Account.Pages.Manage;

public partial class ResetAuthenticator : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] private ILogger<ResetAuthenticator> Logger { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    private async Task OnSubmitAsync()
    {
        var user = await UserManager.GetUserAsync(HttpContext.User);
        if (user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        await UserManager.SetTwoFactorEnabledAsync(user, false);
        await UserManager.ResetAuthenticatorKeyAsync(user);
        var userId = await UserManager.GetUserIdAsync(user);
        Logger.LogInformation("User with ID '{UserId}' has reset their authentication app key.", userId);

        await SignInManager.RefreshSignInAsync(user);

        RedirectManager.RedirectToWithStatus(
            "Account/Manage/EnableAuthenticator",
            "Your authenticator app key has been reset, you will need to configure your authenticator app using the new key.",
            HttpContext);
    }
}