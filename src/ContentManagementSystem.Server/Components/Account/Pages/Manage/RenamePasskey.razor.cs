using System.Buffers.Text;
using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Account.Pages.Manage;

public partial class RenamePasskey : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private User? _user;
    private UserPasskeyInfo? _passkey;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [Parameter]
    public string? Id { get; set; }

    [SupplyParameterFromForm]
    private InputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = (await UserManager.GetUserAsync(HttpContext.User))!;
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        byte[] credentialId;
        try
        {
            credentialId = Base64Url.DecodeFromChars(Id);
        }
        catch (FormatException)
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The specified passkey ID had an invalid format.", HttpContext);
            return;
        }

        _passkey = await UserManager.GetPasskeyAsync(_user, credentialId);
        if (_passkey is null)
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The specified passkey could not be found.", HttpContext);
            return;
        }
    }

    private async Task Rename()
    {
        _passkey!.Name = Input.Name;
        var result = await UserManager.AddOrUpdatePasskeyAsync(_user!, _passkey);
        if (!result.Succeeded)
        {
            RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Error: The passkey could not be updated.", HttpContext);
            return;
        }

        RedirectManager.RedirectToWithStatus("Account/Manage/Passkeys", "Passkey updated successfully.", HttpContext);
    }

    private sealed class InputModel
    {
        [Required]
        [StringLength(200, ErrorMessage = "Passkey names must be no longer than {1} characters.")]
        public string Name { get; set; } = "";
    }
}