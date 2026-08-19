using System.Text;

using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;

namespace ContentManagementSystem.Server.Components.Account.Pages;

public partial class RegisterConfirmation : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private ICmsEmailSender EmailSender { get; set; } = default!;
    [Inject] private IHostEnvironment Environment { get; set; } = default!;
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
        else if (!EmailSender.IsConfigured && Environment.IsDevelopment())
        {
            // The developer's way in while no mail transport is configured (task P7-18): the
            // confirmation link is shown on the page because nothing is going to deliver it.
            //
            // Gated on the environment as well as on the transport, which the scaffolded version was
            // not. A production deployment that forgot to configure SMTP would otherwise hand every
            // visitor a working confirmation link for any address they cared to type.
            var userId = await UserManager.GetUserIdAsync(user);
            var code = await UserManager.GenerateEmailConfirmationTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            _emailConfirmationLink = NavigationManager.GetUriWithQueryParameters(
                NavigationManager.ToAbsoluteUri("Account/ConfirmEmail").AbsoluteUri,
                new Dictionary<string, object?> { ["userId"] = userId, ["code"] = code, ["returnUrl"] = ReturnUrl });
        }
    }
}