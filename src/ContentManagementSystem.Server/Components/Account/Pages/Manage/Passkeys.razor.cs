using System.Buffers.Text;

using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Account.Pages.Manage;

public partial class Passkeys : ComponentBase
{
    [Inject] private UserManager<User> UserManager { get; set; } = default!;
    [Inject] private SignInManager<User> SignInManager { get; set; } = default!;
    [Inject] private IdentityRedirectManager RedirectManager { get; set; } = default!;

    private const int MaxPasskeyCount = 100;

    private User? _user;
    private IList<UserPasskeyInfo>? _currentPasskeys;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [SupplyParameterFromForm]
    private string? Action { get; set; }

    [SupplyParameterFromForm]
    private string? CredentialId { get; set; }

    [SupplyParameterFromForm(FormName = "add-passkey")]
    private PasskeyInputModel Input { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Input ??= new();

        _user = await UserManager.GetUserAsync(HttpContext.User);
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }
        _currentPasskeys = await UserManager.GetPasskeysAsync(_user);
    }

    private async Task AddPasskey()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        if (!string.IsNullOrEmpty(Input.Error))
        {
            RedirectManager.RedirectToCurrentPageWithStatus($"Error: {Input.Error}", HttpContext);
            return;
        }

        if (string.IsNullOrEmpty(Input.CredentialJson))
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: The browser did not provide a passkey.", HttpContext);
            return;
        }

        if (_currentPasskeys!.Count >= MaxPasskeyCount)
        {
            RedirectManager.RedirectToCurrentPageWithStatus($"Error: You have reached the maximum number of allowed passkeys.", HttpContext);
            return;
        }

        var attestationResult = await SignInManager.PerformPasskeyAttestationAsync(Input.CredentialJson);
        if (!attestationResult.Succeeded)
        {
            RedirectManager.RedirectToCurrentPageWithStatus($"Error: Could not add the passkey: {attestationResult.Failure.Message}", HttpContext);
            return;
        }

        var addPasskeyResult = await UserManager.AddOrUpdatePasskeyAsync(_user, attestationResult.Passkey);
        if (!addPasskeyResult.Succeeded)
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: The passkey could not be added to your account.", HttpContext);
            return;
        }

        // Immediately prompt the user to enter a name for the credential
        var credentialIdBase64Url = Base64Url.EncodeToString(attestationResult.Passkey.CredentialId);
        RedirectManager.RedirectTo($"Account/Manage/RenamePasskey/{credentialIdBase64Url}");
    }

    private async Task UpdatePasskey()
    {
        switch (Action)
        {
            case "rename":
                RedirectManager.RedirectTo($"Account/Manage/RenamePasskey/{CredentialId}");
                break;
            case "delete":
                await DeletePasskey();
                break;
            default:
                RedirectManager.RedirectToCurrentPageWithStatus($"Error: Unknown action '{Action}'.", HttpContext);
                break;
        }
    }

    private async Task DeletePasskey()
    {
        if (_user is null)
        {
            RedirectManager.RedirectToInvalidUser(UserManager, HttpContext);
            return;
        }

        byte[] credentialId;
        try
        {
            credentialId = Base64Url.DecodeFromChars(CredentialId);
        }
        catch (FormatException)
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: The specified passkey ID had an invalid format.", HttpContext);
            return;
        }

        var result = await UserManager.RemovePasskeyAsync(_user, credentialId);
        if (!result.Succeeded)
        {
            RedirectManager.RedirectToCurrentPageWithStatus("Error: The passkey could not be deleted.", HttpContext);
            return;
        }

        RedirectManager.RedirectToCurrentPageWithStatus("Passkey deleted successfully.", HttpContext);
    }
}