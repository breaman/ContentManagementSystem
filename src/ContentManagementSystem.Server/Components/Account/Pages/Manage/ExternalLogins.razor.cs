using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Account.Pages.Manage;

public partial class ExternalLogins : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] private IUserStore<User> UserStore { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    public const string LinkLoginCallbackAction = "LinkLoginCallback";

    private User? _user;
    private IList<UserLoginInfo>? _currentLogins;
    private IList<AuthenticationScheme>? _otherLogins;
    private bool _showRemoveButton;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private string? LoginProvider { get; set; }

    [SupplyParameterFromForm]
    private string? ProviderKey { get; set; }

    [SupplyParameterFromQuery]
    private string? Action { get; set; }

    protected override async Task OnInitializedAsync()
    {
        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        _currentLogins = await UserManager.GetLoginsAsync(_user);
        _otherLogins = (await SignInManager.GetExternalAuthenticationSchemesAsync())
            .Where(auth => _currentLogins.All(ul => auth.Name != ul.LoginProvider))
            .ToList();

        string? passwordHash = null;
        if (UserStore is IUserPasswordStore<User> userPasswordStore)
        {
            passwordHash = await userPasswordStore.GetPasswordHashAsync(_user, HttpContext.RequestAborted);
        }

        _showRemoveButton = passwordHash is not null || _currentLogins.Count > 1;

        if (HttpMethods.IsGet(HttpContext.Request.Method) && Action == LinkLoginCallbackAction)
        {
            await OnGetLinkLoginCallbackAsync();
        }
    }

    private async Task OnSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var result = await UserManager.RemoveLoginAsync(_user, LoginProvider!, ProviderKey!);
        if (!result.Succeeded)
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: The external login was not removed.", HttpContext);
        }
        else
        {
            await SignInManager.RefreshSignInAsync(_user);
            RedirectManager.RedirectToCurrentPageWithStatus("The external login was removed.", HttpContext);
        }
    }

    private async Task OnGetLinkLoginCallbackAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        var userId = await UserManager.GetUserIdAsync(_user);
        var info = await SignInManager.GetExternalLoginInfoAsync(userId);
        if (info is null)
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: Could not load external login info.", HttpContext);
            return;
        }

        var result = await UserManager.AddLoginAsync(_user, info);
        if (result.Succeeded)
        {
            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            RedirectManager.RedirectToCurrentPageWithStatus("The external login was added.", HttpContext);
        }
        else
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: The external login was not added. External logins can only be associated with one account.", HttpContext);
        }
    }
}