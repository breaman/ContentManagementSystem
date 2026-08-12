using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ContentManagementSystem.Server.Components.Account.Pages;

public partial class ResendEmailConfirmation : ComponentBase
{
    [Inject] UserManager<User> UserManager { get; set; } = default!;
    [Inject] IEmailSender<User> EmailSender { get; set; } = default!;
    [Inject] NavigationManager NavigationManager { get; set; } = default!;
    [Inject] IdentityRedirectManager RedirectManager { get; set; } = default!;

    private string? _message;

    [SupplyParameterFromForm] private InputModel Input { get; set; } = default!;

    protected override void OnInitialized()
    {
        Input ??= new();
    }

    private async Task OnValidSubmitAsync()
    {
        var user = await UserManager.FindByEmailAsync(Input.Email!);
        if (user is null)
        {
            _message = "Verification email sent. Please check your email.";
            return;
        }

        var userId = await UserManager.GetUserIdAsync(user);
        var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
        code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
        var callbackUrl = NavigationManager.GetUriWithQueryParameters(
            NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
            new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code });
        await EmailSender.SendConfirmationLinkAsync(user, Input.Email, HtmlEncoder.Default.Encode(callbackUrl));

        _message = "Verification email sent. Please check your email.";
    }

    private sealed class InputModel
    {
        [Required][EmailAddress] public string Email { get; set; } = "";
    }
}