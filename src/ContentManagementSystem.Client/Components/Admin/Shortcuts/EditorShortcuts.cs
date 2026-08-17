namespace ContentManagementSystem.Client.Components.Admin.Shortcuts;

/// <summary>
/// The shortcuts the page editor answers to (task P6-23).
/// </summary>
/// <remarks>
/// One list, read by both the listener and the reference dialog. The choices are deliberately
/// conservative: every one of them either matches what the browser or the operating system already
/// means by that chord (Ctrl+S saves) or is unclaimed (<c>?</c> for help, which is the convention
/// half the web already uses). Nothing here overrides a browser shortcut an editor relies on —
/// Ctrl+F, Ctrl+T, Ctrl+W, and the tab-switching chords are all left alone, because a CMS that
/// swallowed the browser's own find is a CMS people fight with.
/// </remarks>
public static class EditorShortcuts
{
    /// <summary>Opens the reference dialog.</summary>
    public const string ShowHelp = "show-help";

    /// <summary>Saves the draft immediately, rather than waiting for autosave.</summary>
    public const string Save = "save";

    /// <summary>Runs the publish checks without publishing.</summary>
    public const string Check = "check";

    /// <summary>Opens the publish dialog.</summary>
    public const string Publish = "publish";

    /// <summary>Opens the draft preview in a new tab.</summary>
    public const string Preview = "preview";

    /// <summary>Every shortcut, in the order the reference dialog lists them.</summary>
    public static IReadOnlyList<KeyboardShortcut> All { get; } =
    [
        new(ShowHelp, "?", "Show this list of shortcuts", "Everywhere", Shift: true),
        new(Save, "s", "Save the draft now", "Editing", Control: true, RequiresEditing: true),
        new(Check, "k", "Check the page before publishing", "Editing", Control: true),
        new(Publish, "p", "Open the publish dialog", "Publishing", Control: true, Shift: true),
        new(Preview, "e", "Open the draft preview in a new tab", "Publishing", Control: true),
    ];

    /// <summary>The shortcuts to show somebody, given what they may do.</summary>
    /// <param name="canEdit">Whether the editor may write to this page.</param>
    /// <returns>The shortcuts that will actually do something for them.</returns>
    /// <remarks>
    /// Filtered rather than greyed out, for the reason the tree's context menu gives: an entry that
    /// cannot act is one a person reads and dismisses every time they open the list.
    /// </remarks>
    public static IReadOnlyList<KeyboardShortcut> For(bool canEdit) =>
        canEdit ? All : [.. All.Where(shortcut => !shortcut.RequiresEditing)];
}
