using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Account.Pages;

public partial class LoginWith2fa : ComponentBase
{
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] UserManager<User> UserManager { get; set; } = default!;
    [Inject] IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] ILogger<LoginWith2fa> Logger { get; set; } = default!;

    private string? _message;
    private User _user = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    [SupplyParameterFromQuery]
    private bool RememberMe { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        // Ensure the user has gone through the username & password screen first
        _user = await SignInManager.GetTwoFactorAuthenticationUserAsync() ??
            throw new InvalidOperationException("Unable to load two-factor authentication user.");
    }

    private async Task OnValidSubmitAsync()
    {
        var authenticatorCode = Input.TwoFactorCode!.Replace(" ", string.Empty).Replace("-", string.Empty);
        var result = await SignInManager.TwoFactorAuthenticatorSignInAsync(authenticatorCode, RememberMe, Input.RememberMachine);
        var userId = await UserManager.GetUserIdAsync(_user);

        if (result.Succeeded)
        {
            Logger.LogInformation("User with ID '{UserId}' logged in with 2fa.", userId);
            RedirectManager.RedirectTo(ReturnUrl);
        }
        else if (result.IsLockedOut)
        {
            Logger.LogWarning("User with ID '{UserId}' account locked out.", userId);
            RedirectManager.RedirectTo("Account/Lockout");
        }
        else
        {
            Logger.LogWarning("Invalid authenticator code entered for user with ID '{UserId}'.", userId);
            _message = "Error: Invalid authenticator code.";
        }
    }

    private sealed class InputModel
    {
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Authenticator code")]
        public string? TwoFactorCode { get; set; }

        [Display(Name = "Remember this machine")]
        public bool RememberMachine { get; set; }
    }
}