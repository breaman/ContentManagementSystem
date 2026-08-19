using System.ComponentModel.DataAnnotations;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Navigation;

/// <summary>
/// One managed menu's entries: what they say, where they go, and in what order (task P8-16).
/// </summary>
/// <remarks>
/// The order is a number an editor types rather than a drag handle. Dragging is nicer and is a Phase
/// 6 pattern this screen deliberately does not borrow: a footer has a handful of links, and the
/// number is unambiguous, keyboard-reachable, and impossible to get wrong by half a pixel.
/// </remarks>
public partial class MenuEditor : ComponentBase
{
    /// <summary>The menu being edited.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes menus.</summary>
    [Inject]
    public INavigationClient Navigation { get; set; } = default!;

    /// <summary>Reports what a refused write said.</summary>
    [Inject]
    public IToastService Toasts { get; set; } = default!;

    /// <summary>The menu, or null while it is still loading.</summary>
    protected NavigationMenuDetail? Menu { get; private set; }

    /// <summary>What the add-entry form holds.</summary>
    protected MenuItemFormModel Draft { get; } = new();

    /// <summary>Whether a write is in flight.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>Errors from the last refused write.</summary>
    protected IReadOnlyList<ApiDiagnostic> Errors { get; private set; } = [];

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync() => Menu = await Navigation.GetMenuAsync(Id);

    /// <summary>The label of another entry, for the "nested under" column.</summary>
    /// <param name="itemId">The entry's id, or null.</param>
    /// <returns>Its label, or null when there is no parent.</returns>
    protected string? Label(int? itemId) =>
        itemId is null ? null : Menu?.Items.FirstOrDefault(item => item.Id == itemId)?.Label;

    /// <summary>Adds the entry the form describes.</summary>
    protected async Task AddItemAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Navigation.AddItemAsync(Id, new SaveNavigationItemRequest
            {
                Label = Draft.Label,
                PageId = Draft.PageId,
                ExternalUrl = Draft.ExternalUrl,
                OpenInNewTab = Draft.OpenInNewTab,
                SortOrder = Draft.SortOrder,
            });

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Menu = result.Value;
            Draft.Clear();

            Toasts.ShowSuccess("Entry added.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Removes an entry, and anything nested under it.</summary>
    /// <param name="item">The entry.</param>
    protected async Task DeleteItemAsync(NavigationItemDetail item)
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Navigation.DeleteItemAsync(Id, item.Id);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Menu = result.Value;

            Toasts.ShowSuccess($"'{item.Label}' removed.");
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>What the add-entry form binds to.</summary>
public sealed class MenuItemFormModel
{
    /// <summary>The link text.</summary>
    [Required]
    public string Label { get; set; } = string.Empty;

    /// <summary>The page it points at, or null for an external link.</summary>
    public int? PageId { get; set; }

    /// <summary>Where it goes when it is not a page.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>Whether it opens in a new browsing context.</summary>
    public bool OpenInNewTab { get; set; }

    /// <summary>Order among siblings.</summary>
    public int SortOrder { get; set; }

    /// <summary>Empties the form after a successful save.</summary>
    public void Clear()
    {
        Label = string.Empty;
        PageId = null;
        ExternalUrl = null;
        OpenInNewTab = false;
        SortOrder = 0;
    }
}
