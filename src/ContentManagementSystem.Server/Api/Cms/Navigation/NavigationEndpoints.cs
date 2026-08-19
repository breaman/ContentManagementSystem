using ContentManagementSystem.Core.Navigation;
using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Navigation;

/// <summary>
/// <c>/api/cms/v1/navigation</c> — managed menus and their entries (task P8-16).
/// </summary>
/// <remarks>
/// Writes require <c>Content.Publish</c>, for the reason the redirect endpoints give: a menu change
/// reaches anonymous visitors as soon as it is saved, with no draft and no publish step in between.
/// <para>
/// Structural navigation has no endpoints at all. It is derived from the content tree, so there is
/// nothing to edit here that is not edited by moving, publishing, or hiding a page.
/// </para>
/// </remarks>
public static class NavigationEndpoints
{
    /// <summary>Path segment this resource hangs off.</summary>
    public const string Prefix = "/navigation/menus";

    /// <summary>
    /// Maps the navigation endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapNavigationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var menus = group.MapGroup(Prefix).WithTags("Navigation");

        menus.MapGet("/", ListAsync)
            .WithName("ListNavigationMenus")
            .WithSummary("Lists the managed menus.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        menus.MapGet("/{id:int}", GetAsync)
            .WithName("GetNavigationMenu")
            .WithSummary("Reads one menu and its entries.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        menus.MapPost("/", CreateAsync)
            .WithName("CreateNavigationMenu")
            .WithSummary("Creates a menu.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        menus.MapPut("/{id:int}", UpdateAsync)
            .WithName("UpdateNavigationMenu")
            .WithSummary("Renames a menu. Its key is fixed — templates hold it.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        menus.MapDelete("/{id:int}", DeleteAsync)
            .WithName("DeleteNavigationMenu")
            .WithSummary("Deletes a menu and everything in it.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        menus.MapPost("/{id:int}/items", AddItemAsync)
            .WithName("AddNavigationItem")
            .WithSummary("Adds an entry to a menu.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        menus.MapPut("/{id:int}/items/{itemId:int}", UpdateItemAsync)
            .WithName("UpdateNavigationItem")
            .WithSummary("Replaces an entry of a menu.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        menus.MapDelete("/{id:int}/items/{itemId:int}", DeleteItemAsync)
            .WithName("DeleteNavigationItem")
            .WithSummary("Removes an entry, and anything nested under it.")
            .RequireAuthorization(CmsPermissions.ContentPublish)
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> ListAsync(
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.ListAsync(cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> GetAsync(
        int id,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.GetAsync(id, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> CreateAsync(
        CreateNavigationMenuRequest request,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.CreateAsync(request, cancellationToken))
        .ToHttpResult(detail => Results.Created($"{CmsApiEndpoints.BasePath}{Prefix}/{detail.Id}", detail));

    private static async Task<IResult> UpdateAsync(
        int id,
        UpdateNavigationMenuRequest request,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.UpdateAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> DeleteAsync(
        int id,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.DeleteAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());

    private static async Task<IResult> AddItemAsync(
        int id,
        SaveNavigationItemRequest request,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.AddItemAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> UpdateItemAsync(
        int id,
        int itemId,
        SaveNavigationItemRequest request,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.UpdateItemAsync(id, itemId, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> DeleteItemAsync(
        int id,
        int itemId,
        INavigationMenuService menus,
        CancellationToken cancellationToken) =>
        (await menus.DeleteItemAsync(id, itemId, cancellationToken)).ToHttpResult(Results.Ok);
}
