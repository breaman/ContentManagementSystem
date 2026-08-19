namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A hand-managed menu: an ordered list of links that does not mirror the content tree
/// (spec section 10.7).
/// </summary>
/// <remarks>
/// The second of the two navigation mechanisms, and it exists because the first cannot do this.
/// Structural navigation is generated from the tree and costs nothing to maintain, which is right
/// for a primary menu and wrong for a footer — where "Privacy", "Careers", and a link to a partner's
/// site sit together in an order nobody could derive from where those pages live.
/// <para>
/// <see cref="Key"/> rather than the id is what content and templates name, and it is what the
/// <c>nav:{menuKey}</c> cache tag is built from — so a menu can be rebuilt in a new environment
/// without every reference to it breaking (spec section 16.2).
/// </para>
/// </remarks>
public class NavigationMenu : FingerPrintEntityBase
{
    /// <summary>Stable key a template asks for the menu by, such as <c>footer</c>.</summary>
    public string Key { get; set; } = null!;

    /// <summary>Editor-facing name.</summary>
    public string Name { get; set; } = null!;

    /// <summary>What the menu is for, shown in the menu admin screen.</summary>
    public string? Description { get; set; }

    /// <summary>The menu's items, ordered by <see cref="NavigationItem.SortOrder"/>.</summary>
    public ICollection<NavigationItem> Items { get; set; } = [];
}
