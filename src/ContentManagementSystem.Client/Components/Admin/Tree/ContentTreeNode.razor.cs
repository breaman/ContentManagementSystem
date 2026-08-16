using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// One row of the content tree, and the level beneath it (task P6-02).
/// </summary>
/// <remarks>
/// Recursive, and stateless: everything it draws — whether it is open, whether its children have
/// arrived, whether it holds the tab stop — is asked of <see cref="ContentTree"/>. That is what lets
/// the tree refresh one level, or move focus across a level boundary, without the rows having to
/// agree with each other about anything.
/// </remarks>
public partial class ContentTreeNode : ComponentBase, IDisposable
{
    /// <summary>How far one level is indented from the one above it, in rem.</summary>
    private const decimal IndentStep = 1.125m;

    /// <summary>The page on this row.</summary>
    [Parameter]
    [EditorRequired]
    public PageSummary Page { get; set; } = default!;

    /// <summary>Depth within this tree, counting the root level as one, for <c>aria-level</c>.</summary>
    [Parameter]
    public int Level { get; set; } = 1;

    /// <summary>Position among its siblings, counting from one, for <c>aria-posinset</c>.</summary>
    [Parameter]
    public int Position { get; set; } = 1;

    /// <summary>How many siblings this level has, for <c>aria-setsize</c>.</summary>
    [Parameter]
    public int SetSize { get; set; } = 1;

    /// <summary>
    /// Parent of the level this row sits in, or null for the root of the site.
    /// </summary>
    /// <remarks>
    /// Passed down rather than read from <c>Page.ParentId</c>, so the drop gaps either side of the
    /// row belong to the level as the tree drew it. The two agree today; they would stop agreeing
    /// the first time a screen rendered a filtered or partial level, and a gap that reported the
    /// wrong parent would move a page somewhere nobody pointed at.
    /// </remarks>
    [Parameter]
    public int? ParentId { get; set; }

    /// <summary>The tree that owns the expansion, selection, and focus state.</summary>
    [CascadingParameter]
    private ContentTree Tree { get; set; } = default!;

    /// <summary>Whether a dragged page is currently hovering over this row.</summary>
    private bool _dropTarget;

    /// <summary>The element the tree focuses when the arrow keys land here.</summary>
    private ElementReference Row { get; set; }

    /// <summary>
    /// What <c>aria-expanded</c> should say, or null to leave it off entirely.
    /// </summary>
    /// <remarks>
    /// A leaf must not carry the attribute at all. <c>aria-expanded="false"</c> on a row with
    /// nothing under it announces "collapsed", which invites a keyboard user to press the right
    /// arrow and find that nothing happens.
    /// </remarks>
    private string? ExpandedAttribute =>
        Page.HasChildren ? Aria(Tree.IsExpanded(Page.Id)) : null;

    /// <summary>The row's indent, which is drawn rather than nested.</summary>
    /// <remarks>
    /// Padding on the row instead of margin on the list, so the whole row — including the part left
    /// of the title — stays a click target at every depth.
    /// </remarks>
    private string Indent =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"padding-inline-start: {(Level - 1) * IndentStep:0.###}rem");

    /// <inheritdoc />
    protected override void OnAfterRender(bool firstRender) => Tree.RegisterRow(Page.Id, Row);

    /// <inheritdoc />
    public void Dispose()
    {
        Tree.UnregisterRow(Page.Id);

        GC.SuppressFinalize(this);
    }

    /// <summary>Renders a boolean as an ARIA attribute value, which is lowercase.</summary>
    private static string Aria(bool value) => value ? "true" : "false";

    /// <summary>Pairs each child with its one-based position, which <c>aria-posinset</c> needs.</summary>
    /// <remarks>
    /// Materialized rather than computed per row: inside <c>Virtualize</c> only a window of the list
    /// is rendered, so a row cannot know its own index without searching the list for itself, which
    /// would turn a 500-sibling level into a quadratic render.
    /// </remarks>
    /// <remarks>
    /// A <see cref="List{T}"/> rather than a read-only interface because <c>Virtualize</c> takes an
    /// <c>ICollection&lt;T&gt;</c>.
    /// </remarks>
    private static List<IndexedPage> Indexed(IReadOnlyList<PageSummary> children) =>
        [.. children.Select((page, index) => new IndexedPage(page, index + 1))];

    /// <summary>Opens or closes this node.</summary>
    private Task ToggleAsync() => Tree.ToggleAsync(Page);

    /// <summary>Selects this row and tells the host to open it.</summary>
    private Task ActivateAsync() => Tree.ActivateAsync(Page);

    /// <summary>Takes a dropped page in as a child of this one.</summary>
    private async Task DropIntoAsync()
    {
        _dropTarget = false;

        await Tree.DropIntoAsync(Page);
    }

    /// <summary>A child paired with its position among its siblings.</summary>
    /// <param name="Page">The child page.</param>
    /// <param name="Position">Its one-based position, for <c>aria-posinset</c>.</param>
    private readonly record struct IndexedPage(PageSummary Page, int Position);
}
