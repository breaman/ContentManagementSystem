using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pickers;

/// <summary>
/// Chooses a file from the media library, in a dialog (task P6-15, spec section 14.3).
/// </summary>
/// <remarks>
/// Thin on purpose. <c>MediaBrowser</c> is already the picker — it has the folders, the filters, the
/// search, and the inline uploader P6-15 asks for, because the library screen and the <c>media</c>
/// field's control were built as one component in P5-22. All this adds is the dialog around it, so
/// that a link picker and a rich-text toolbar can open the library without giving up the screen they
/// are on.
/// <para>
/// The recycle bin is off. Choosing a deleted file would author a reference to something the delete
/// guard has already decided nothing points at.
/// </para>
/// </remarks>
public partial class MediaPicker : ComponentBase
{
    /// <summary>Whether the picker is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Heading of the dialog.</summary>
    [Parameter]
    public string Title { get; set; } = "Choose a file";

    /// <summary>Restrict the grid to one kind of file, as an image-only property does.</summary>
    [Parameter]
    public string? RestrictToKind { get; set; }

    /// <summary>Raised with the chosen item.</summary>
    [Parameter]
    public EventCallback<MediaDetail> OnPicked { get; set; }

    /// <summary>Raised when the editor backs out.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }
}
