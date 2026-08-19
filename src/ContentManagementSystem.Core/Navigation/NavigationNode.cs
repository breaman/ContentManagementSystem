namespace ContentManagementSystem.Core.Navigation;

/// <summary>
/// One entry in a rendered navigation menu (spec section 10.7).
/// </summary>
/// <param name="Label">The link text.</param>
/// <param name="Url">Where it goes — site-relative for a page, absolute for an external link.</param>
/// <param name="PageId">The page it points at, or null for an external link.</param>
/// <param name="OpenInNewTab">Whether the link opens in a new browsing context.</param>
/// <param name="Children">Entries nested below this one.</param>
/// <remarks>
/// Already resolved: a node exists only if a visitor following it reaches something. Unpublished
/// pages are absent rather than present-and-disabled, which is what makes "navigation reflects
/// publish state" a property of the query rather than of every renderer remembering to check
/// (acceptance criterion P8 #9).
/// </remarks>
public sealed record NavigationNode(
    string Label,
    string Url,
    int? PageId,
    bool OpenInNewTab,
    IReadOnlyList<NavigationNode> Children)
{
    /// <summary>An entry with nothing beneath it.</summary>
    /// <param name="label">The link text.</param>
    /// <param name="url">Where it goes.</param>
    /// <param name="pageId">The page it points at, or null.</param>
    /// <param name="openInNewTab">Whether it opens in a new browsing context.</param>
    /// <returns>The node.</returns>
    public static NavigationNode Leaf(string label, string url, int? pageId = null, bool openInNewTab = false) =>
        new(label, url, pageId, openInNewTab, []);
}
