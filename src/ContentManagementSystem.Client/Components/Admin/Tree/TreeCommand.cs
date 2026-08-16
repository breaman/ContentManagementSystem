namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// One entry of the tree's context menu (task P6-04, spec section 14.2).
/// </summary>
/// <param name="Id">Stable identifier, used as the DOM id of the item and in tests.</param>
/// <param name="Label">What the editor reads.</param>
/// <param name="Icon">Bootstrap icon class.</param>
/// <param name="IsDestructive">Whether the item should be drawn as dangerous.</param>
/// <remarks>
/// The menu is built from data rather than written out as markup, because it is assembled per row:
/// a page with nothing published has no "Unpublish", and "Paste" only exists once something has been
/// copied. A list the component filters is easier to keep honest than a wall of <c>@if</c> blocks,
/// and it gives the keyboard handler a single ordered sequence to walk.
/// </remarks>
public sealed record TreeCommand(string Id, string Label, string Icon, bool IsDestructive = false)
{
    /// <summary>Create a child page under this one.</summary>
    public static TreeCommand NewChild { get; } = new("new-child", "New child page", "bi-file-earmark-plus");

    /// <summary>Copy this page alone, beside itself.</summary>
    public static TreeCommand Duplicate { get; } = new("duplicate", "Duplicate", "bi-files");

    /// <summary>Copy this page and everything beneath it.</summary>
    public static TreeCommand DuplicateDeep { get; } =
        new("duplicate-deep", "Duplicate with children", "bi-diagram-3");

    /// <summary>Put this page on the tree's clipboard, to be pasted as a copy.</summary>
    public static TreeCommand Copy { get; } = new("copy", "Copy", "bi-clipboard");

    /// <summary>Put this page on the tree's clipboard, to be pasted as a move.</summary>
    public static TreeCommand Cut { get; } = new("cut", "Move to…", "bi-scissors");

    /// <summary>Paste whatever is on the clipboard inside this page.</summary>
    public static TreeCommand Paste { get; } = new("paste", "Paste inside", "bi-clipboard-check");

    /// <summary>Publish this page's draft.</summary>
    public static TreeCommand Publish { get; } = new("publish", "Publish", "bi-cloud-upload");

    /// <summary>Take this page off the public site.</summary>
    public static TreeCommand Unpublish { get; } = new("unpublish", "Unpublish", "bi-cloud-slash");

    /// <summary>Send this page and its subtree to the recycle bin.</summary>
    public static TreeCommand Delete { get; } = new("delete", "Delete…", "bi-trash", IsDestructive: true);
}

/// <summary>
/// Whether the page on the tree's clipboard is waiting to be copied or moved.
/// </summary>
/// <remarks>
/// One clipboard with a mode rather than two clipboards. An editor holds one page at a time, and the
/// question a paste asks — copy or move — is a property of how it got there, not of where it goes.
/// </remarks>
public enum ClipboardMode
{
    /// <summary>Pasting produces a deep copy and leaves the original where it is.</summary>
    Copy = 0,

    /// <summary>Pasting moves the page, with the usual URL confirmation.</summary>
    Move = 1,
}
