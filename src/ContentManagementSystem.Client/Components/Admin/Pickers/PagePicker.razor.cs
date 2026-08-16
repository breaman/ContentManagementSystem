using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>
/// Chooses a page from the content tree (task P6-15, spec section 14.3).
/// </summary>
/// <remarks>
/// Two ways in, because editors arrive holding two different things. Somebody linking to a sibling
/// knows where it sits and browses the tree; somebody linking to a page they were sent a ticket
/// about knows its title or its id and searches. Offering only the tree makes the second person walk
/// a site they do not have a map of.
/// <para>
/// <strong>The tree is lazy and the search is not.</strong> The tree fetches one level per expansion,
/// which is what keeps it usable at the five thousand pages acceptance criterion P6 #7 names; the
/// search asks the server, which is the only thing that can see the levels nobody has expanded.
/// </para>
/// <para>
/// A page the slot does not allow is shown and disabled rather than hidden. An editor who cannot
/// find the page they were told to link to needs to know it is refused, not that it is missing.
/// </para>
/// </remarks>
public partial class PagePicker : ComponentBase
{
    /// <summary>How long to wait after the last keystroke before searching.</summary>
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(250);

    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Whether the picker is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Heading of the dialog, so a link picker and a page reference can name themselves.</summary>
    [Parameter]
    public string Title { get; set; } = "Choose a page";

    /// <summary>
    /// Template keys the slot accepts, empty when it accepts any.
    /// </summary>
    /// <remarks>
    /// The <c>allowedTemplates</c> setting on <c>pageReference</c>. Enforced by the publish check as
    /// well; offering the choice here is what stops an editor discovering the rule at publish time.
    /// </remarks>
    [Parameter]
    public IReadOnlyList<string> AllowedTemplates { get; set; } = [];

    /// <summary>Pages that may not be chosen — the page being edited, and anything already picked.</summary>
    [Parameter]
    public IReadOnlyCollection<int> Excluded { get; set; } = [];

    /// <summary>Raised with the chosen page.</summary>
    [Parameter]
    public EventCallback<PageSummary> OnPicked { get; set; }

    /// <summary>Raised when the editor backs out.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>The site's root pages, or null while loading.</summary>
    private IReadOnlyList<PageTreeNode>? Roots { get; set; }

    /// <summary>What has been chosen but not yet confirmed.</summary>
    private PageSummary? Selected { get; set; }

    /// <summary>What is in the search box.</summary>
    private string? Search { get; set; }

    /// <summary>What the last search returned.</summary>
    private IReadOnlyList<PageSummary> Results { get; set; } = [];

    /// <summary>Whether a search is in flight.</summary>
    private bool IsSearching { get; set; }

    /// <summary>Cancels the search a newer keystroke has superseded.</summary>
    private CancellationTokenSource? _search;

    /// <summary>Distinguishes this picker's control ids from another's on the same page.</summary>
    private string PickerId { get; } = $"page-picker-{Guid.NewGuid():n}";

    /// <inheritdoc />
    /// <remarks>
    /// The tree is loaded when the dialog opens rather than when the component is created. A page
    /// with twelve link properties on it would otherwise make twelve requests for the same root
    /// level before anybody clicked anything.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (!IsOpen || Roots is not null) return;

        Roots = await Client.GetTreeAsync(parentId: null, depth: 1);
    }

    /// <summary>What the template restriction means, in words.</summary>
    private string? Restriction => AllowedTemplates.Count switch
    {
        0 => null,
        1 => $"Only pages using the {AllowedTemplates[0]} template can be chosen here.",
        _ => $"Only pages using {string.Join(", ", AllowedTemplates)} can be chosen here.",
    };

    /// <summary>Whether a page may be chosen.</summary>
    private bool IsAllowed(PageSummary page) =>
        !Excluded.Contains(page.Id) &&
        (AllowedTemplates.Count == 0 ||
         AllowedTemplates.Contains(page.TemplateKey, StringComparer.Ordinal));

    private bool IsChosen(PageSummary page) => Selected?.Id == page.Id;

    /// <summary>Searches after the typing pauses.</summary>
    /// <remarks>
    /// Debounced rather than searching per keystroke: the query is a <c>LIKE</c> over two columns on
    /// the server, and a five-letter title would otherwise cost five of them, four of which are
    /// already stale when they return.
    /// </remarks>
    private async Task OnSearchAsync(ChangeEventArgs args)
    {
        Search = args.Value?.ToString();

        _search?.Cancel();
        _search?.Dispose();

        if (string.IsNullOrWhiteSpace(Search))
        {
            _search = null;
            Results = [];
            IsSearching = false;

            return;
        }

        var cancellation = _search = new CancellationTokenSource();
        var term = Search;

        IsSearching = true;

        try
        {
            await Task.Delay(SearchDebounce, cancellation.Token);

            var page = await Client.ListAsync(new PageQuery(Search: term, Limit: 50), cancellation.Token);

            if (cancellation.IsCancellationRequested) return;

            Results = page.Items;
            IsSearching = false;
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke owns the box now; it will set the state when it lands.
        }
    }

    private async Task ConfirmAsync()
    {
        if (Selected is not { } page) return;

        await OnPicked.InvokeAsync(page);

        Selected = null;
    }
}
