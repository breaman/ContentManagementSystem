using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;


using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The content tree of spec section 14.2 (task P6-02).
/// </summary>
/// <remarks>
/// A real tree widget rather than the flattened two-level table <c>PageList</c> shipped in Phase 2:
/// children are fetched a level at a time as they are opened, long sibling lists are virtualized,
/// and every row reports its publishing state.
/// <para>
/// <strong>Loaded children live here, not in the nodes.</strong> The tree endpoint returns nested
/// records, but keeping the nesting as the source of truth would mean rebuilding an immutable object
/// graph on every expansion and every refresh. A dictionary keyed on parent id makes "the children
/// of 42 have arrived" a single assignment, and makes refreshing one subtree after a move or a
/// delete a matter of dropping one key.
/// </para>
/// <para>
/// <strong>Keyboard navigation follows the tree pattern, not tab order.</strong> One row is
/// focusable at a time — the roving <c>tabindex</c> — so Tab moves past the whole tree rather than
/// through five thousand pages, and the arrow keys move within it. That is what makes P6-37's
/// keyboard-only pass survivable, and it is why the row is the focusable element and holds no
/// buttons of its own: a control inside a <c>treeitem</c> is another Tab stop per row.
/// </para>
/// </remarks>
public partial class ContentTree : ComponentBase
{
    /// <summary>Stands in for the site root when keying children by parent.</summary>
    private const int RootKey = 0;

    /// <summary>
    /// How many siblings a level may have before it is rendered through <c>Virtualize</c>.
    /// </summary>
    /// <remarks>
    /// Below this, virtualization costs more than it saves — it adds spacer elements and a scroll
    /// subscription to a list the browser would lay out instantly. Above it, the 500-siblings case
    /// P6-35 gates is what the tree has to survive.
    /// </remarks>
    public const int VirtualizeThreshold = 60;

    /// <summary>Reads the tree, a level at a time.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Decides whether a scheduled publish is still in the future.</summary>
    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>Confirms a move once it has happened, which the tree itself cannot show.</summary>
    [Inject]
    private IToastService Toasts { get; set; } = default!;

    /// <summary>The page currently open in the canvas, so the tree can show where the editor is.</summary>
    [Parameter]
    public int? SelectedId { get; set; }

    /// <summary>Raised when a row is activated — by click, or by Enter on the focused row.</summary>
    [Parameter]
    public EventCallback<PageSummary> OnActivated { get; set; }

    /// <summary>Accessible name of the tree.</summary>
    [Parameter]
    public string Label { get; set; } = "Content tree";

    /// <summary>The root level, or null while it is still loading.</summary>
    [PersistentState]
    public IReadOnlyList<PageTreeNode>? Roots { get; set; }

    /// <summary>Children that have been fetched, keyed on their parent's id.</summary>
    private readonly Dictionary<int, IReadOnlyList<PageSummary>> _children = [];

    /// <summary>Which nodes are open.</summary>
    private readonly HashSet<int> _expanded = [];

    /// <summary>Which nodes have a fetch in flight, so the row can say so.</summary>
    private readonly HashSet<int> _loading = [];

    /// <summary>The rows currently in the document, so focus can be moved to one.</summary>
    private readonly Dictionary<int, ElementReference> _rows = [];

    /// <summary>Which row holds the tree's single tab stop.</summary>
    private int? _activeId;

    /// <summary>A row that should take focus after the next render.</summary>
    private int? _focusWanted;

    /// <summary>Why the last fetch failed, if it did.</summary>
    private string? _error;

    /// <summary>The current time, as the status classifier needs it.</summary>
    internal DateTimeOffset Now => Clock.GetUtcNow();

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Roots is null)
        {
            await LoadRootsAsync();
        }
        else
        {
            Seed(RootKey, Roots);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_focusWanted is not { } id)
        {
            return;
        }

        _focusWanted = null;

        // Absent when the row is scrolled out of a virtualized level. Nothing to do about that: the
        // browser cannot focus an element that is not in the document, and the arrow keys that got
        // here scroll it into view first in every case that matters.
        if (_rows.TryGetValue(id, out var row))
        {
            try
            {
                await row.FocusAsync();
            }
            catch (InvalidOperationException)
            {
                // The element went away between the render and this call — a refresh landing at the
                // same moment as a key press.
            }
        }
    }

    /// <summary>Re-reads one level, discarding what was cached for it.</summary>
    /// <param name="parentId">The node whose children changed, or null for the root level.</param>
    /// <remarks>
    /// Called after a move, a delete, or a create. It reloads one level rather than the whole tree,
    /// because everything else the editor had open is still true.
    /// </remarks>
    public async Task RefreshAsync(int? parentId = null)
    {
        if (parentId is not { } id)
        {
            await LoadRootsAsync();

            return;
        }

        _children.Remove(id);

        if (_expanded.Contains(id))
        {
            await LoadChildrenAsync(id);
        }

        StateHasChanged();
    }

    /// <summary>Opens the tree down to a page, so a newly created or moved page is visible.</summary>
    /// <param name="ancestorIds">The page's ancestors, outermost first.</param>
    public async Task ExpandToAsync(IEnumerable<int> ancestorIds)
    {
        ArgumentNullException.ThrowIfNull(ancestorIds);

        foreach (var id in ancestorIds)
        {
            _expanded.Add(id);

            await LoadChildrenAsync(id);
        }

        StateHasChanged();
    }

    /// <summary>Whether a node is open.</summary>
    internal bool IsExpanded(int id) => _expanded.Contains(id);

    /// <summary>Whether a node's children are on their way.</summary>
    internal bool IsLoading(int id) => _loading.Contains(id);

    /// <summary>Whether a node is the page open in the canvas.</summary>
    internal bool IsSelected(int id) => SelectedId == id;

    /// <summary>Whether a node holds the tree's single tab stop.</summary>
    internal bool IsActive(int id) => (_activeId ?? FirstVisibleId()) == id;

    /// <summary>The children a node has had fetched, or an empty list.</summary>
    internal IReadOnlyList<PageSummary> ChildrenOf(int id) =>
        _children.TryGetValue(id, out var children) ? children : [];

    /// <summary>Remembers a row's element so focus can be moved to it.</summary>
    internal void RegisterRow(int id, ElementReference row) => _rows[id] = row;

    /// <summary>Forgets a row that has left the document.</summary>
    internal void UnregisterRow(int id) => _rows.Remove(id);

    /// <summary>Opens or closes a node, fetching its children the first time it opens.</summary>
    internal async Task ToggleAsync(PageSummary page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (!_expanded.Add(page.Id))
        {
            _expanded.Remove(page.Id);

            return;
        }

        if (!_children.ContainsKey(page.Id))
        {
            await LoadChildrenAsync(page.Id);
        }
    }

    /// <summary>Makes a row the active one and tells the host it was activated.</summary>
    internal async Task ActivateAsync(PageSummary page)
    {
        ArgumentNullException.ThrowIfNull(page);

        _activeId = page.Id;

        await OnActivated.InvokeAsync(page);
    }

    /// <summary>
    /// Moves the tree's focus, opens and closes nodes, and activates a row (spec section 28).
    /// </summary>
    /// <remarks>
    /// Handled on the tree rather than per row so that the flattened order the arrows walk is
    /// computed once, from the same state that rendered the rows. A per-row handler would have to
    /// ask its parent for its neighbours anyway, and would get the answer wrong at the seam between
    /// two levels.
    /// </remarks>
    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        var visible = Visible().ToList();

        if (visible.Count == 0)
        {
            return;
        }

        var currentId = _activeId ?? visible[0].Page.Id;
        var index = visible.FindIndex(row => row.Page.Id == currentId);

        if (index < 0)
        {
            index = 0;
        }

        var current = visible[index];

        // Alt is the modifier that turns "move me" into "move the page". Checked before the
        // navigation keys, since the two share all four arrows (task P6-03).
        if (args.AltKey && await TryKeyboardMoveAsync(args, current.Page))
        {
            return;
        }

        switch (args.Key)
        {
            case "ArrowDown":
                Focus(visible[Math.Min(index + 1, visible.Count - 1)].Page.Id);

                break;

            case "ArrowUp":
                Focus(visible[Math.Max(index - 1, 0)].Page.Id);

                break;

            case "Home":
                Focus(visible[0].Page.Id);

                break;

            case "End":
                Focus(visible[^1].Page.Id);

                break;

            case "ArrowRight":
                // Closed and expandable: open it. Already open: step into the first child. That
                // second behaviour is what makes the right arrow a single "go deeper" gesture.
                if (current.Page.HasChildren && !IsExpanded(current.Page.Id))
                {
                    await ToggleAsync(current.Page);
                }
                else if (index + 1 < visible.Count && visible[index + 1].Level > current.Level)
                {
                    Focus(visible[index + 1].Page.Id);
                }

                break;

            case "ArrowLeft":
                // Open: close it. Closed: step out to the parent, which is the nearest row above at
                // a shallower level.
                if (IsExpanded(current.Page.Id))
                {
                    await ToggleAsync(current.Page);
                }
                else
                {
                    for (var above = index - 1; above >= 0; above--)
                    {
                        if (visible[above].Level < current.Level)
                        {
                            Focus(visible[above].Page.Id);

                            break;
                        }
                    }
                }

                break;

            case "Enter":
            case " ":
                await ActivateAsync(current.Page);

                break;

            default:
                return;
        }
    }

    /// <summary>Marks a row as the one to focus after the next render.</summary>
    private void Focus(int id)
    {
        _activeId = id;
        _focusWanted = id;
    }

    /// <summary>The first row in the tree, which holds the tab stop before anything is focused.</summary>
    private int? FirstVisibleId() => Roots is { Count: > 0 } roots ? roots[0].Page.Id : null;

    /// <summary>
    /// Walks the open parts of the tree in the order they are painted.
    /// </summary>
    /// <returns>Every visible row, with the depth it is drawn at.</returns>
    /// <remarks>
    /// Depth comes from the walk rather than from <c>PageSummary.Depth</c>, which is the page's depth
    /// in the whole site. The two agree today because the tree always starts at the root; they would
    /// stop agreeing the moment a screen showed a subtree, and the arrow keys depend on this one.
    /// </remarks>
    private IEnumerable<VisibleRow> Visible()
    {
        foreach (var root in Roots ?? [])
        {
            foreach (var row in Walk(root.Page, level: 1))
            {
                yield return row;
            }
        }
    }

    /// <summary>The recursive half of <see cref="Visible"/>.</summary>
    private IEnumerable<VisibleRow> Walk(PageSummary page, int level)
    {
        yield return new VisibleRow(page, level);

        if (!IsExpanded(page.Id))
        {
            yield break;
        }

        foreach (var child in ChildrenOf(page.Id))
        {
            foreach (var row in Walk(child, level + 1))
            {
                yield return row;
            }
        }
    }

    /// <summary>Fetches the root level.</summary>
    private async Task LoadRootsAsync()
    {
        try
        {
            _error = null;

            var roots = await Client.GetTreeAsync(depth: 1);

            Roots = roots;

            Seed(RootKey, roots);
        }
        catch (HttpRequestException exception)
        {
            _error = exception.Message;
        }
    }

    /// <summary>Fetches one node's children.</summary>
    private async Task LoadChildrenAsync(int parentId)
    {
        if (!_loading.Add(parentId))
        {
            return;
        }

        try
        {
            _error = null;

            var nodes = await Client.GetTreeAsync(parentId, depth: 1);

            _children[parentId] = [.. nodes.Select(node => node.Page)];
        }
        catch (HttpRequestException exception)
        {
            // The node stays expanded and empty with the error shown, rather than silently
            // collapsing: an editor who opened a node and saw it shut again learns nothing.
            _error = exception.Message;
        }
        finally
        {
            _loading.Remove(parentId);
        }
    }

    /// <summary>Records a fetched level in the children lookup.</summary>
    private void Seed(int parentId, IReadOnlyList<PageTreeNode> nodes) =>
        _children[parentId] = [.. nodes.Select(node => node.Page)];

    /// <summary>One row of the flattened walk the arrow keys move through.</summary>
    /// <param name="Page">The page on the row.</param>
    /// <param name="Level">Its depth within this tree, counting the root level as one.</param>
    private readonly record struct VisibleRow(PageSummary Page, int Level);
}
