namespace ContentManagementSystem.Core.Navigation;

/// <summary>
/// The two navigation mechanisms of spec section 10.7 (tasks P8-15, P8-16).
/// </summary>
/// <remarks>
/// Both are read-only, both return published content only, and both are cached with the
/// <c>nav:{menuKey}</c> tag by whatever renders them — which is why a publish, an unpublish, or a
/// move enqueues those tags alongside the page's own (task P8-17).
/// </remarks>
public interface INavigationService
{
    /// <summary>
    /// Builds navigation from the content tree.
    /// </summary>
    /// <param name="maxDepth">How many levels below the root to include, at least one.</param>
    /// <param name="rootPageId">Page to start from, or null for the top level of the site.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The nodes, in sibling order.</returns>
    /// <remarks>
    /// Filtered by <c>Page.ShowInNavigation</c> and by publish state, and the two are different
    /// switches: the first is an editor saying "not in the menu", the second is the site saying
    /// "not yet". A page that fails either is absent along with everything beneath it — a menu entry
    /// whose parent cannot be reached is a link into a hole.
    /// </remarks>
    Task<IReadOnlyList<NavigationNode>> GetStructuralAsync(
        int maxDepth = 2,
        int? rootPageId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a hand-managed menu.
    /// </summary>
    /// <param name="menuKey">The menu's key, such as <c>footer</c>.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The nodes, in the order an editor put them in. Empty when no such menu exists.</returns>
    Task<IReadOnlyList<NavigationNode>> GetMenuAsync(
        string menuKey,
        CancellationToken cancellationToken = default);
}
