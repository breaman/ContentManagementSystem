using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>
/// One node of the page picker's tree, fetching its children when it is opened (task P6-15).
/// </summary>
/// <remarks>
/// A separate component from the backoffice's own <c>ContentTree</c>, deliberately. That one carries
/// status indicators, a context menu, drag reordering, a filter, and a roving <c>tabindex</c> — every
/// one of which is right for navigating a site and wrong inside a dialog whose whole job is to
/// return one id. Reusing it would mean a picker in which an editor can delete a page.
/// <para>
/// Children are fetched on first expansion and kept, so collapsing and reopening a branch while
/// looking for something costs nothing.
/// </para>
/// </remarks>
public partial class PagePickerBranch : ComponentBase
{
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>This node and whatever children arrived with it.</summary>
    [Parameter]
    [EditorRequired]
    public PageTreeNode Node { get; set; } = default!;

    /// <summary>How deep this node sits, for <c>aria-level</c>.</summary>
    [Parameter]
    public int Level { get; set; } = 1;

    /// <summary>What is currently chosen, so this node can mark itself.</summary>
    [Parameter]
    public PageSummary? Selected { get; set; }

    /// <summary>Whether a page may be chosen.</summary>
    [Parameter]
    [EditorRequired]
    public Func<PageSummary, bool> IsAllowed { get; set; } = _ => true;

    /// <summary>Raised when a page in this subtree is chosen.</summary>
    [Parameter]
    public EventCallback<PageSummary> OnSelected { get; set; }

    /// <summary>Whether this node's children are showing.</summary>
    private bool IsExpanded { get; set; }

    /// <summary>The children, or null while they are being fetched.</summary>
    private IReadOnlyList<PageTreeNode>? Children { get; set; }

    /// <summary>Opens or closes the node, fetching its children the first time it opens.</summary>
    private async Task ToggleAsync()
    {
        IsExpanded = !IsExpanded;

        if (!IsExpanded || Children is not null) return;

        // The children the parent fetch happened to include, when it included any. A node returned
        // at the bottom of the requested depth has an empty list and HasChildren set, which is the
        // distinction PageTreeNode documents and the reason this cannot key off Count alone.
        Children = Node.Children.Count > 0
            ? Node.Children
            : await Client.GetTreeAsync(Node.Page.Id, depth: 1);
    }
}
