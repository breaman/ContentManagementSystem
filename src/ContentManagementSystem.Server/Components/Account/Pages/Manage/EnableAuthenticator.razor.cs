using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Account.Pages.Manage;

public partial class EnableAuthenticator : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private UrlEncoder UrlEncoder { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;
    [Inject] private ILogger<EnableAuthenticator> Logger { get; set; } = default!;

    private const string AuthenticatorUriFormat = "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6";

    private string? _message;
    private User? _user;
    private string? _sharedKey;
    private string? _authenticatorUri;
    private IEnumerable<string>? _recoveryCodes;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        await LoadSharedKeyAndQrCodeUriAsync(_user);
    }

    private async Task OnValidSubmitAsync()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        // Strip spaces and hyphens
        var verificationCode = Input.Code.Replace(" ", string.Empty).Replace("-", string.Empty);

        var is2faTokenValid = await UserManager.VerifyTwoFactorTokenAsync(
            _user, UserManager.Options.Tokens.AuthenticatorTokenProvider, verificationCode);

        if (!is2faTokenValid)
        {
            _message = "Error: Verification code is invalid.";
            return;
        }

        await UserManager.SetTwoFactorEnabledAsync(_user, true);
        var userId = await UserManager.GetUserIdAsync(_user);
        Logger.LogInformation("User with ID '{UserId}' has enabled 2FA with an authenticator app.", userId);

        _message = "Your authenticator app has been verified.";

        if (await UserManager.CountRecoveryCodesAsync(_user) == 0)
        {
            _recoveryCodes = await UserManager.GenerateNewTwoFactorRecoveryCodesAsync(_user, 10);
        }
        else
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/TwoFactorAuthentication", _message, HttpContext);
        }
    }

    private async ValueTask LoadSharedKeyAndQrCodeUriAsync(User user)
    {
        // Load the authenticator key & QR code URI to display on the form
        var unformattedKey = await UserManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await UserManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await UserManager.GetAuthenticatorKeyAsync(user);
        }

        _sharedKey = FormatKey(unformattedKey!);

        var email = await UserManager.GetEmailAsync(user);
        _authenticatorUri = GenerateQrCodeUri(email!, unformattedKey!);
    }

    private string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        int currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }
        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    private string GenerateQrCodeUri(string email, string unformattedKey)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            AuthenticatorUriFormat,
            UrlEncoder.Encode("Microsoft.AspNetCore.Identity.UI"),
            UrlEncoder.Encode(email),
            unformattedKey);
    }

    private sealed class InputModel
    {
        [Required]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Verification Code")]
        public string Code { get; set; } = "";
    }
}