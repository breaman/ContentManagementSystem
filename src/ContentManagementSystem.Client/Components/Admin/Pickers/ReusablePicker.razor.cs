using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>
/// What a reusable placement was chosen to be.
/// </summary>
/// <param name="Item">The item placed.</param>
/// <param name="PinnedVersionId">
/// The version row the placement is pinned to, or null to follow the item — which is the default and
/// the whole point of late binding (ADR-0004).
/// </param>
public sealed record ReusablePick(ReusableContentSummary Item, int? PinnedVersionId);

/// <summary>
/// Chooses reusable content to place, and decides whether the placement follows it
/// (task P6-15, spec section 9).
/// </summary>
/// <remarks>
/// <strong>The pin is part of choosing, not a setting to find afterwards.</strong> A placement that
/// follows the item and one pinned to a version behave completely differently the first time
/// somebody republishes a shared banner — forty pages change, or one does not — and offering the
/// decision at the moment of placement is the only point at which an author is thinking about it.
/// <para>
/// The pin resolves to a version <em>row id</em> here rather than travelling as a version number.
/// The field type stores an id and the resolver looks it up with the item id in the same predicate,
/// so a pin quoting another item's version resolves to nothing instead of rendering the wrong
/// content under this item's cache tag.
/// </para>
/// </remarks>
public partial class ReusablePicker : ComponentBase
{
    /// <summary>How long to wait after the last keystroke before searching.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    [Inject]
    private IReusableClient Client { get; set; } = default!;

    /// <summary>Whether the picker is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>
    /// Block type keys the slot accepts, empty when it accepts any.
    /// </summary>
    /// <remarks>
    /// The <c>allowedTypes</c> setting, which holds block type keys because a reusable item's shape
    /// is a block type. The field type cannot enforce it — "what shape is item 3" is not answerable
    /// from the stored value — so the publish check does, and this offers it.
    /// </remarks>
    [Parameter]
    public IReadOnlyList<string> AllowedTypes { get; set; } = [];

    /// <summary>Raised with the chosen item and its pin.</summary>
    [Parameter]
    public EventCallback<ReusablePick> OnPicked { get; set; }

    /// <summary>Raised when the editor backs out.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>The library, filtered by whatever is in the search box, or null while loading.</summary>
    private IReadOnlyList<ReusableContentSummary>? Items { get; set; }

    /// <summary>What has been chosen but not yet confirmed.</summary>
    private ReusableContentSummary? Selected { get; set; }

    /// <summary>Whether the placement should be pinned to the version published right now.</summary>
    private bool IsPinned { get; set; }

    /// <summary>What is in the search box.</summary>
    private string? Search { get; set; }

    /// <summary>Cancels the search a newer keystroke has superseded.</summary>
    private CancellationTokenSource? _search;

    /// <summary>Distinguishes this picker's control ids from another's on the same page.</summary>
    private string PickerId { get; } = $"reusable-picker-{Guid.NewGuid():n}";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (!IsOpen || Items is not null) return;

        Items = await Client.ListAsync();
    }

    /// <summary>What the block type restriction means, in words.</summary>
    private string? Restriction => AllowedTypes.Count switch
    {
        0 => null,
        1 => $"Only {AllowedTypes[0]} content can be placed here.",
        _ => $"Only {string.Join(", ", AllowedTypes)} content can be placed here.",
    };

    /// <summary>Whether an item may be placed in this slot.</summary>
    private bool IsAllowed(ReusableContentSummary item) =>
        AllowedTypes.Count == 0 || AllowedTypes.Contains(item.BlockTypeKey, StringComparer.Ordinal);

    /// <summary>Searches the library after the typing pauses.</summary>
    private async Task OnSearchAsync(ChangeEventArgs args)
    {
        Search = args.Value?.ToString();

        _search?.Cancel();
        _search?.Dispose();

        var cancellation = _search = new CancellationTokenSource();
        var term = string.IsNullOrWhiteSpace(Search) ? null : Search;

        try
        {
            await Task.Delay(SearchDebounce, cancellation.Token);

            var results = await Client.ListAsync(search: term, cancellationToken: cancellation.Token);

            if (cancellation.IsCancellationRequested) return;

            Items = results;
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the box now; it will set the state when it lands.
        }
    }

    /// <summary>
    /// Confirms the choice, resolving the pin to the id of the version that is published.
    /// </summary>
    /// <remarks>
    /// The lookup happens here rather than in the editor because this is where the author asked for
    /// it, and because an item with nothing published has nothing to pin to — in which case the
    /// placement follows the item, which is what "show it once it is published" means and what the
    /// author would have chosen if they had been asked twice.
    /// </remarks>
    private async Task ConfirmAsync()
    {
        if (Selected is not { } item) return;

        int? pinned = null;

        if (IsPinned && item.PublishedVersionNumber is not null)
        {
            var versions = await Client.GetVersionsAsync(item.Id);

            pinned = versions.FirstOrDefault(version => version.IsPublished)?.Id;
        }

        await OnPicked.InvokeAsync(new ReusablePick(item, pinned));

        Selected = null;
        IsPinned = false;
    }
}
