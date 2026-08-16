using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Common;

/// <summary>Which of the three surfaces of spec section 14.4 an editor is showing.</summary>
public enum EditorMode
{
    /// <summary>The source, or the WYSIWYG surface, alone.</summary>
    Edit,

    /// <summary>The rendering alone.</summary>
    Preview,

    /// <summary>Both, side by side, scrolling together.</summary>
    Split,
}

/// <summary>
/// Chooses between Edit, Preview, and Split (tasks P6-08 and P6-10, spec section 14.4).
/// </summary>
/// <remarks>
/// Its own component because two editors offer the same three modes and the accessible shape of the
/// control is the part most easily got wrong: three buttons that merely look pressed announce
/// nothing, while a radio group announces which one is chosen, how many there are, and moves
/// between them with the arrow keys without any code.
/// </remarks>
public partial class EditorModeSwitch : ComponentBase
{
    /// <summary>The mode currently showing.</summary>
    [Parameter]
    public EditorMode Mode { get; set; }

    /// <summary>Raised with the chosen mode.</summary>
    [Parameter]
    public EventCallback<EditorMode> ModeChanged { get; set; }

    /// <summary>
    /// The modes on offer.
    /// </summary>
    /// <remarks>
    /// Split is dropped on a narrow viewport by the surrounding layout rather than here — two panes
    /// side by side below a tablet are two panes too narrow to read — but the choice itself stays
    /// the same three everywhere it is offered.
    /// </remarks>
    [Parameter]
    public IReadOnlyList<EditorMode> Modes { get; set; } =
        [EditorMode.Edit, EditorMode.Preview, EditorMode.Split];

    private static string Label(EditorMode mode) => mode switch
    {
        EditorMode.Edit => "Write",
        EditorMode.Preview => "Preview",
        _ => "Both",
    };

    private static string Icon(EditorMode mode) => mode switch
    {
        EditorMode.Edit => "bi-pencil",
        EditorMode.Preview => "bi-eye",
        _ => "bi-layout-split",
    };

    private static string Hint(EditorMode mode) => mode switch
    {
        EditorMode.Edit => "The source on its own",
        EditorMode.Preview => "What the page will show",
        _ => "Both, scrolling together",
    };
}
