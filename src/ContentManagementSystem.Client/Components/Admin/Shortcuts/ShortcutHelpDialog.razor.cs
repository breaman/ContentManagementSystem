using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Shortcuts;

/// <summary>
/// The shortcut reference dialog (task P6-23).
/// </summary>
/// <remarks>
/// Rendered from the same table the listener matches against, so the list cannot document a chord
/// that does nothing or omit one that works. It is reachable by the shortcut it documents, which is
/// the convention, and by nothing else — deliberately: a permanent "keyboard shortcuts" button in the
/// action bar would occupy space beside Publish for a dialog people open twice.
/// <para>
/// Every entry is an accelerator for a control that also exists as a button. That is stated at the
/// top of the dialog rather than assumed, because the reason it matters is spec section 28's rule
/// that nothing is reachable only by a chord somebody has to have learnt.
/// </para>
/// </remarks>
public partial class ShortcutHelpDialog : ComponentBase
{
    /// <summary>Whether the dialog is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>The shortcuts to list.</summary>
    [Parameter]
    public IReadOnlyList<KeyboardShortcut> Shortcuts { get; set; } = [];

    /// <summary>Raised when the editor closes it.</summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    /// <summary>The shortcuts by section, in the order they were declared.</summary>
    private IEnumerable<IGrouping<string, KeyboardShortcut>> Grouped =>
        Shortcuts.GroupBy(shortcut => shortcut.Group);

    /// <summary>The chord split into the keys it is made of, one per <c>kbd</c>.</summary>
    private static IReadOnlyList<string> Keys(KeyboardShortcut shortcut) =>
        [.. shortcut.Chord.Split(" + ")];
}
