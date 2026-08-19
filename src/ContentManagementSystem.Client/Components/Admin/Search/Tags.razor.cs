using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Search;

/// <summary>
/// The tag vocabulary, with rename, merge, and delete (task P8-20, spec section 17.1).
/// </summary>
/// <remarks>
/// Tags are added on a page, not here: this screen exists for the housekeeping a free-form
/// vocabulary always eventually needs — the near-duplicate that wants merging, the label somebody
/// mistyped, the tag nothing uses any more.
/// <para>
/// The page count is a link into the search screen's tag filter rather than a number on its own, so
/// "what is actually tagged this" is one click from the decision to rename or delete it.
/// </para>
/// </remarks>
public partial class Tags : ComponentBase
{
    /// <summary>Reads and writes the tag vocabulary.</summary>
    [Inject]
    public ISearchClient Search { get; set; } = default!;

    /// <summary>Reports what a refused write said.</summary>
    [Inject]
    public IToastService Toasts { get; set; } = default!;

    /// <summary>The tags, or null while they are still loading.</summary>
    protected IReadOnlyList<TagSummary>? Items { get; private set; }

    /// <summary>The tag being renamed, or null when none is.</summary>
    protected int? EditingId { get; private set; }

    /// <summary>What the rename box holds.</summary>
    protected string NewName { get; set; } = string.Empty;

    /// <summary>Whether a write is in flight, which disables the buttons that would start another.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>Errors from the last refused write.</summary>
    protected IReadOnlyList<ApiDiagnostic> Errors { get; private set; } = [];

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => await LoadAsync();

    /// <summary>Opens the rename box on one tag.</summary>
    protected void StartRename(TagSummary tag)
    {
        EditingId = tag.Id;
        NewName = tag.Name;
        Errors = [];
    }

    /// <summary>Closes the rename box without saving.</summary>
    protected void CancelRename()
    {
        EditingId = null;
        NewName = string.Empty;
    }

    /// <summary>Renames the tag, merging it when the new name already exists.</summary>
    protected async Task RenameAsync(TagSummary tag)
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Search.RenameTagAsync(tag.Id, new RenameTagRequest { Name = NewName });

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            CancelRename();
            await LoadAsync();

            // The merge is named outright. An editor who typed an existing name deserves to be told
            // that two tags became one rather than discovering it from a count that moved.
            Toasts.ShowSuccess(result.Value!.Merged
                ? $"'{tag.Name}' was merged into '{result.Value.Tag.Name}' across {result.Value.PagesAffected} page(s)."
                : $"'{tag.Name}' was renamed to '{result.Value.Tag.Name}' across {result.Value.PagesAffected} page(s).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Deletes the tag and takes it off every page carrying it.</summary>
    protected async Task DeleteAsync(TagSummary tag)
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Search.DeleteTagAsync(tag.Id);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            await LoadAsync();

            Toasts.ShowSuccess($"'{tag.Name}' was deleted and removed from {result.Value} page(s).");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadAsync() => Items = await Search.GetTagsAsync();
}
