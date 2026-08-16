using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Media;

/// <summary>
/// One folder in the library's tree, and everything beneath it (task P5-22).
/// </summary>
/// <remarks>
/// Recursive, and its own component for that reason: a folder's children are folders, so the markup
/// that draws one draws all of them. Flattening the tree into a list with indentation would lose the
/// structure a screen reader announces from the nesting.
/// </remarks>
public partial class MediaFolderBranch : ComponentBase
{
    /// <summary>The folder to draw.</summary>
    [Parameter]
    [EditorRequired]
    public MediaFolderNode Folder { get; set; } = default!;

    /// <summary>The folder currently being listed, so this one can mark itself.</summary>
    [Parameter]
    public int? SelectedId { get; set; }

    /// <summary>Raised when this folder, or one beneath it, is chosen.</summary>
    [Parameter]
    public EventCallback<int?> OnSelect { get; set; }

    /// <summary>Whether this is the folder being listed.</summary>
    private bool IsSelected => SelectedId == Folder.Id;
}
