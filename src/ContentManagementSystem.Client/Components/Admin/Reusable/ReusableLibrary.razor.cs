using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Reusable;

/// <summary>
/// The reusable content library: what exists, and how to make more (task P4-11).
/// </summary>
/// <remarks>
/// Shaped like the page list on purpose. An editor who has learned that a page has a draft and a
/// separately published version should not have to learn it again here, because it is the same
/// mechanism — the only difference is that this content has no address of its own.
/// <para>
/// The state column is the one thing worth reading carefully. "Unpublished changes" on a page means
/// visitors see the older version of <em>that page</em>; here it means visitors see the older version
/// on every page placing the item, which is a much larger fact hiding behind identical words.
/// </para>
/// </remarks>
public partial class ReusableLibrary : ComponentBase
{
    /// <summary>Reads and writes items, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IReusableClient Client { get; set; } = default!;

    /// <summary>The library, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<ReusableContentSummary>? Items { get; set; }

    /// <summary>Block types a new item can be shaped by, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<BlockTypeSummary>? BlockTypes { get; set; }

    /// <summary>What the search box holds.</summary>
    private string? Search { get; set; }

    /// <summary>The item being created.</summary>
    private NewItem Draft { get; } = new();

    /// <summary>Why the last write did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Anything non-blocking the last write reported.</summary>
    private IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Items ??= await Client.ListAsync();
        BlockTypes ??= await Client.GetBlockTypesAsync();
    }

    private async Task OnSearchChangedAsync(ChangeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        Search = args.Value?.ToString();

        // Filtered by the database rather than in the browser: the library is unbounded, and a
        // client-side filter would be a promise that the whole of it had been downloaded first.
        Items = await Client.ListAsync(search: Search);
    }

    private async Task CreateAsync()
    {
        IsBusy = true;
        Errors = null;
        Warnings = null;

        try
        {
            var result = await Client.CreateAsync(new CreateReusableContentRequest(
                Draft.BlockTypeId,
                Draft.Name,
                string.IsNullOrWhiteSpace(Draft.Key) ? null : Draft.Key,
                string.IsNullOrWhiteSpace(Draft.Description) ? null : Draft.Description));

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Warnings = result.Warnings;
            Draft.Reset();
            Items = await Client.ListAsync(search: Search);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>The create form's own model, so an empty form is not a half-built request.</summary>
    private sealed class NewItem
    {
        /// <summary>Editor-facing display name.</summary>
        public string? Name { get; set; }

        /// <summary>Stable key, or blank to generate one from the name.</summary>
        public string? Key { get; set; }

        /// <summary>Help text describing when to reach for the item.</summary>
        public string? Description { get; set; }

        /// <summary>Block type shaping the item. Zero until one is chosen.</summary>
        public int BlockTypeId { get; set; }

        /// <summary>Clears the form after a successful create.</summary>
        public void Reset()
        {
            Name = null;
            Key = null;
            Description = null;
            BlockTypeId = 0;
        }
    }
}
