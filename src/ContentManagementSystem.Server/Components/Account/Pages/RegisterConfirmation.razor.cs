using System.Text;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Components.Email;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ContentManagementSystem.Server.Components.Account.Pages;

public partial class RegisterConfirmation : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private IEmailSender<User> EmailSender { get; set; } = default!;
    [Inject] private NavigationManager NavigationManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private string? _emailConfirmationLink;
    private string? _statusMessage;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromQuery]
    private string? Email { get; set; }

    [SupplyParameterFromQuery]
    private string? ReturnUrl { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Email is null)
        {
            RedirectManager.RedirectTo("");
            return;
        }

        var user = await UserManager.FindByEmailAsync(Email);
        if (user is null)
        {
            HttpContext.Response.StatusCode = StatusCodes.Status404NotFound;
            _statusMessage = "Error finding user for unspecified email";
        }
        else if (EmailSender is IdentityNoOpEmailSender)
        {
            // Once you add a real email sender, you should remove this code that lets you confirm the account
            var userId = await UserManager.GetUserIdAsync(user);
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            _emailConfirmationLink = NavigationManager.GetUriWithQueryParameters(
                NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code, ["returnUrl"] = ReturnUrl });
        }
    }
}