using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The strip between two tree rows that a dragged page can be dropped into (task P6-03).
/// </summary>
/// <remarks>
/// Reordering and reparenting are two different intentions, and a drag has to say which. Dropping
/// onto a <em>row</em> means "put it inside this page"; dropping into the gap <em>between</em> two
/// rows means "put it here, among these siblings". Splitting them into two targets is what avoids
/// the usual alternative — measuring where in a row's height the pointer let go, which needs the
/// element's geometry, guesses at the editor's intent, and is ambiguous by a few pixels either way.
/// </remarks>
public partial class DropGap : ComponentBase
{
    /// <summary>Parent of the level this gap sits in, or null for the root of the site.</summary>
    [Parameter]
    public int? ParentId { get; set; }

    /// <summary>Index within that level: a page dropped here lands at this position.</summary>
    [Parameter]
    public int Position { get; set; }

    /// <summary>The tree that owns the drag.</summary>
    [Parameter]
    [EditorRequired]
    public ContentTree Tree { get; set; } = default!;

    /// <summary>Whether the pointer is currently over this gap, so it can show itself.</summary>
    private bool _over;

    /// <summary>Drops the dragged page into this gap.</summary>
    private async Task DropAsync()
    {
        _over = false;

        await Tree.DropBeforeAsync(ParentId, Position);
    }
}
