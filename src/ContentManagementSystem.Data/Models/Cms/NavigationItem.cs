namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One entry in a managed menu: either a page in this site, or a URL somewhere else
/// (spec section 10.7).
/// </summary>
/// <remarks>
/// An internal item stores <see cref="PageId"/> and never a URL, which is the same rule internal
/// links in content follow (ADR 0006): the page's address is resolved when the menu renders, so
/// moving or renaming a page moves its menu entry with it rather than leaving a link that 404s.
/// <para>
/// Exactly one of <see cref="PageId"/> and <see cref="ExternalUrl"/> is set, enforced by a check
/// constraint. A row with both would have two answers to "where does this go" and a row with
/// neither would render a link to nowhere.
/// </para>
/// </remarks>
public class NavigationItem : FingerPrintEntityBase
{
    /// <summary>Menu this item belongs to.</summary>
    public int NavigationMenuId { get; set; }

    /// <summary>Menu this item belongs to.</summary>
    public NavigationMenu Menu { get; set; } = null!;

    /// <summary>Parent item, or null for a top-level entry.</summary>
    /// <remarks>
    /// One level of nesting is what a footer's column headings and a utility menu's dropdown need.
    /// Depth is not enforced by the schema; the renderer stops descending, so an item nested deeper
    /// than the design supports is invisible rather than broken.
    /// </remarks>
    public int? ParentId { get; set; }

    /// <summary>Parent item, or null for a top-level entry.</summary>
    public NavigationItem? Parent { get; set; }

    /// <summary>Items nested under this one.</summary>
    public ICollection<NavigationItem> Children { get; set; } = [];

    /// <summary>
    /// The link text.
    /// </summary>
    /// <remarks>
    /// Stored rather than taken from the target page's title. A menu label is often shorter than the
    /// page it points at — "Prices" for "Pricing and plans" — and a menu whose labels changed
    /// whenever somebody retitled a page would reflow on its own.
    /// </remarks>
    public string Label { get; set; } = null!;

    /// <summary>Page this item points at, or null for an external link.</summary>
    public int? PageId { get; set; }

    /// <summary>Page this item points at.</summary>
    public Page? Page { get; set; }

    /// <summary>Absolute URL this item points at, or null for an internal link.</summary>
    public string? ExternalUrl { get; set; }

    /// <summary>Whether the link opens in a new browsing context.</summary>
    public bool OpenInNewTab { get; set; }

    /// <summary>Order among siblings.</summary>
    public int SortOrder { get; set; }
}
