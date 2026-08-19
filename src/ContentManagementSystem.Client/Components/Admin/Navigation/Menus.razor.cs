using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Navigation;

/// <summary>
/// The managed-menu list, and the form that creates one (task P8-16).
/// </summary>
/// <remarks>
/// Deliberately plain. A menu is a key, a name, and a list of links, and the screen that edits one
/// should not be more elaborate than the thing it edits — the entries themselves are the
/// <see cref="MenuEditor"/>'s job.
/// </remarks>
public partial class Menus : ComponentBase
{
    /// <summary>Reads and writes menus.</summary>
    [Inject]
    public INavigationClient Navigation { get; set; } = default!;

    /// <summary>Reports what a refused write said.</summary>
    [Inject]
    public IToastService Toasts { get; set; } = default!;

    /// <summary>The menus, or null while they are still loading.</summary>
    protected IReadOnlyList<NavigationMenuSummary>? Items { get; private set; }

    /// <summary>What the create form holds.</summary>
    protected MenuFormModel Draft { get; } = new();

    /// <summary>Whether a write is in flight, which disables the buttons that would start another.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>Errors from the last refused write.</summary>
    protected IReadOnlyList<ApiDiagnostic> Errors { get; private set; } = [];

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => await LoadAsync();

    /// <summary>Creates the menu the form describes.</summary>
    protected async Task CreateAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Navigation.CreateMenuAsync(new CreateNavigationMenuRequest
            {
                Key = Draft.Key,
                Name = Draft.Name,
                Description = Draft.Description,
            });

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Draft.Clear();
            await LoadAsync();

            Toasts.ShowSuccess($"Menu '{result.Value!.Key}' created.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Deletes a menu and everything in it.</summary>
    /// <param name="menu">The menu.</param>
    protected async Task DeleteAsync(NavigationMenuSummary menu)
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Navigation.DeleteMenuAsync(menu.Id);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            await LoadAsync();

            Toasts.ShowSuccess($"Menu '{menu.Key}' deleted.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync() => Items = await Navigation.GetMenusAsync();
}

/// <summary>What the create form binds to.</summary>
/// <remarks>
/// A mutable model beside the immutable request record, because <c>EditForm</c> binds to properties
/// it can set and the wire contract should not gain setters for the sake of a form.
/// </remarks>
public sealed class MenuFormModel
{
    /// <summary>Stable key a template asks for the menu by.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string Key { get; set; } = string.Empty;

    /// <summary>Editor-facing name.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>What the menu is for.</summary>
    public string? Description { get; set; }

    /// <summary>Empties the form after a successful save.</summary>
    public void Clear()
    {
        Key = string.Empty;
        Name = string.Empty;
        Description = null;
    }
}
