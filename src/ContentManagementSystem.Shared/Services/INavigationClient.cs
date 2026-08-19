using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the menu admin screen needs, wherever it happens to be running (task P8-16).
/// </summary>
/// <remarks>
/// Two implementations, like every other client here: one over HTTP for the WebAssembly backoffice,
/// and one over the services directly for pre-rendering — a request to itself would need a cookie it
/// does not have and an antiforgery token that has not been issued yet.
/// <para>
/// Writes return <see cref="StructureClientResult{T}"/>. It is named for the screens it was written
/// for and is not specific to them: what a backoffice screen needs from a write is whether it
/// worked and what to show, which is the same question here.
/// </para>
/// </remarks>
public interface INavigationClient
{
    /// <summary>Lists the managed menus.</summary>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<IReadOnlyList<NavigationMenuSummary>> GetMenusAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one menu and its entries.</summary>
    /// <param name="id">The menu.</param>
    /// <param name="cancellationToken">Token observed while reading.</param>
    Task<NavigationMenuDetail?> GetMenuAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a menu.</summary>
    /// <param name="request">Its key, name, and description.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<NavigationMenuDetail>> CreateMenuAsync(
        CreateNavigationMenuRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Renames a menu.</summary>
    /// <param name="id">The menu.</param>
    /// <param name="request">The new name and description.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<NavigationMenuDetail>> UpdateMenuAsync(
        int id,
        UpdateNavigationMenuRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a menu and everything in it.</summary>
    /// <param name="id">The menu.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<int>> DeleteMenuAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds an entry to a menu.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="request">The entry.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<NavigationMenuDetail>> AddItemAsync(
        int menuId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces an entry.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="itemId">The entry.</param>
    /// <param name="request">Its new contents.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<NavigationMenuDetail>> UpdateItemAsync(
        int menuId,
        int itemId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an entry, and anything nested under it.</summary>
    /// <param name="menuId">The menu.</param>
    /// <param name="itemId">The entry.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<NavigationMenuDetail>> DeleteItemAsync(
        int menuId,
        int itemId,
        CancellationToken cancellationToken = default);
}
