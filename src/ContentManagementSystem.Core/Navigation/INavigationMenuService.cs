using ContentManagementSystem.Shared.Contracts.Navigation;

namespace ContentManagementSystem.Core.Navigation;

/// <summary>
/// Editing managed menus (task P8-16, spec section 10.7).
/// </summary>
/// <remarks>
/// Writes require the publish permission rather than the edit one, for the reason redirects do: a
/// menu changes what anonymous visitors see the moment it is saved, with no draft, no preview, and
/// no publish step in between (spec section 21.1).
/// <para>
/// Every write enqueues the menu's <c>nav:{key}</c> eviction inside its own save, so a page
/// rendering the menu stops showing the old one within a cache generation.
/// </para>
/// </remarks>
public interface INavigationMenuService
{
    /// <summary>Lists every menu.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<CmsResult<IReadOnlyList<NavigationMenuSummary>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads one menu and its entries.</summary>
    /// <param name="id">The menu.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<CmsResult<NavigationMenuDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a menu.</summary>
    /// <param name="request">Its key, name, and description.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<NavigationMenuDetail>> CreateAsync(
        CreateNavigationMenuRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a menu.</summary>
    /// <param name="id">The menu.</param>
    /// <param name="request">The new name and description.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<NavigationMenuDetail>> UpdateAsync(
        int id,
        UpdateNavigationMenuRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a menu and everything in it.</summary>
    /// <param name="id">The menu.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds an entry to a menu.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="request">The entry.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<NavigationMenuDetail>> AddItemAsync(
        int menuId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces an entry.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="itemId">The entry.</param>
    /// <param name="request">Its new contents.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<NavigationMenuDetail>> UpdateItemAsync(
        int menuId,
        int itemId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an entry, and anything nested under it.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="itemId">The entry.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<NavigationMenuDetail>> DeleteItemAsync(
        int menuId,
        int itemId,
        CancellationToken cancellationToken = default);
}
