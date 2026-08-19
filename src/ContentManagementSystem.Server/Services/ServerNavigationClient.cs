using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Navigation;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="INavigationClient"/>, over the menu service directly (task P8-16).
/// </summary>
/// <param name="menus">Menu reads and writes.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used during pre-rendering, so the menu screen arrives with its content in the HTML rather than
/// showing a spinner until the WebAssembly runtime has downloaded. The service checks the caller's
/// permissions itself, so the shortcut past the API changes nothing about who may do what.
/// </remarks>
public sealed class ServerNavigationClient(INavigationMenuService menus, PrerenderGate gate)
    : INavigationClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<NavigationMenuSummary>> GetMenusAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => menus.ListAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<NavigationMenuDetail?> GetMenuAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => menus.GetAsync(id, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<NavigationMenuDetail>> CreateMenuAsync(
        CreateNavigationMenuRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => menus.CreateAsync(request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<NavigationMenuDetail>> UpdateMenuAsync(
        int id,
        UpdateNavigationMenuRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => menus.UpdateAsync(id, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<int>> DeleteMenuAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => menus.DeleteAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<NavigationMenuDetail>> AddItemAsync(
        int menuId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => menus.AddItemAsync(menuId, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<NavigationMenuDetail>> UpdateItemAsync(
        int menuId,
        int itemId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => menus.UpdateItemAsync(menuId, itemId, request, token),
            cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<NavigationMenuDetail>> DeleteItemAsync(
        int menuId,
        int itemId,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => menus.DeleteItemAsync(menuId, itemId, token),
            cancellationToken));

    /// <summary>Narrows a service result to what a screen needs from it.</summary>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess
            ? StructureClientResult<T>.Success(
                result.Value!,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
