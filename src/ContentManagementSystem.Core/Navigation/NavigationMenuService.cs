using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Navigation;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Navigation;

/// <inheritdoc cref="INavigationMenuService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="cacheInvalidation">Enqueues the menu's eviction inside the same save.</param>
/// <param name="logger">Log for every menu change, which is a public change.</param>
public sealed class NavigationMenuService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    ICacheInvalidationQueue cacheInvalidation,
    ILogger<NavigationMenuService> logger) : INavigationMenuService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<NavigationMenuSummary>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<NavigationMenuSummary>>.Forbidden(
                "Reading navigation is not permitted.",
                NavigationCodes.Forbidden);
        }

        var menus = await context.NavigationMenus
            .AsNoTracking()
            .OrderBy(menu => menu.Key)
            .Select(menu => new NavigationMenuSummary(
                menu.Id,
                menu.Key,
                menu.Name,
                menu.Description,
                menu.Items.Count))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<NavigationMenuSummary>>.Success(menus);
    }

    /// <inheritdoc />
    public async Task<CmsResult<NavigationMenuDetail>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden("Reading navigation is not permitted.");
        }

        return await DetailAsync(id, cancellationToken) is { } detail
            ? CmsResult<NavigationMenuDetail>.Success(detail)
            : NotFound(id);
    }

    /// <inheritdoc />
    public async Task<CmsResult<NavigationMenuDetail>> CreateAsync(
        CreateNavigationMenuRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden("Editing navigation is not permitted.");
        }

        var key = request.Key.Trim();

        if (await context.NavigationMenus.AnyAsync(menu => menu.Key == key, cancellationToken))
        {
            return CmsResult<NavigationMenuDetail>.Invalid(
                NavigationCodes.DuplicateKey,
                $"A menu with the key '{key}' already exists.",
                nameof(CreateNavigationMenuRequest.Key));
        }

        var created = new NavigationMenu
        {
            Key = key,
            Name = request.Name.Trim(),
            Description = Trim(request.Description),
        };

        context.NavigationMenus.Add(created);

        // A menu nobody has rendered yet still gets its tag evicted: a template asking for a key
        // that did not exist rendered nothing, and that nothing is cached like anything else.
        cacheInvalidation.Enqueue([CacheTags.Navigation(key)]);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Navigation menu {MenuKey} was created.", key);

        return CmsResult<NavigationMenuDetail>.Success((await DetailAsync(created.Id, cancellationToken))!);
    }

    /// <inheritdoc />
    public async Task<CmsResult<NavigationMenuDetail>> UpdateAsync(
        int id,
        UpdateNavigationMenuRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden("Editing navigation is not permitted.");
        }

        var menu = await context.NavigationMenus.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (menu is null) return NotFound(id);

        menu.Name = request.Name.Trim();
        menu.Description = Trim(request.Description);

        cacheInvalidation.Enqueue([CacheTags.Navigation(menu.Key)]);

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<NavigationMenuDetail>.Success((await DetailAsync(id, cancellationToken))!);
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return CmsResult<int>.Forbidden("Editing navigation is not permitted.", NavigationCodes.Forbidden);
        }

        var menu = await context.NavigationMenus
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (menu is null)
        {
            return CmsResult<int>.NotFound($"No navigation menu has id {id}.", NavigationCodes.NotFound);
        }

        var removed = menu.Items.Count;

        // Nested entries first: the self-reference is Restrict, so a parent cannot be removed while
        // a child still points at it.
        context.NavigationItems.RemoveRange(menu.Items.Where(item => item.ParentId is not null));
        context.NavigationItems.RemoveRange(menu.Items.Where(item => item.ParentId is null));
        context.NavigationMenus.Remove(menu);

        cacheInvalidation.Enqueue([CacheTags.Navigation(menu.Key)]);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Navigation menu {MenuKey} was deleted along with {ItemCount} item(s).",
            menu.Key,
            removed);

        return CmsResult<int>.Success(removed);
    }

    /// <inheritdoc />
    public async Task<CmsResult<NavigationMenuDetail>> AddItemAsync(
        int menuId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default) =>
        await SaveItemAsync(menuId, itemId: null, request, cancellationToken);

    /// <inheritdoc />
    public async Task<CmsResult<NavigationMenuDetail>> UpdateItemAsync(
        int menuId,
        int itemId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken = default) =>
        await SaveItemAsync(menuId, itemId, request, cancellationToken);

    /// <inheritdoc />
    public async Task<CmsResult<NavigationMenuDetail>> DeleteItemAsync(
        int menuId,
        int itemId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden("Editing navigation is not permitted.");
        }

        var menu = await context.NavigationMenus
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == menuId, cancellationToken);

        if (menu is null) return NotFound(menuId);

        if (menu.Items.FirstOrDefault(item => item.Id == itemId) is not { } target)
        {
            return CmsResult<NavigationMenuDetail>.NotFound(
                $"No item has id {itemId} in this menu.",
                NavigationCodes.NotFound);
        }

        // Children go with the parent. Leaving them behind would reparent them to the top level,
        // which is a menu nobody arranged.
        context.NavigationItems.RemoveRange(menu.Items.Where(item => item.ParentId == itemId));
        context.NavigationItems.Remove(target);

        cacheInvalidation.Enqueue([CacheTags.Navigation(menu.Key)]);

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<NavigationMenuDetail>.Success((await DetailAsync(menuId, cancellationToken))!);
    }

    private async Task<CmsResult<NavigationMenuDetail>> SaveItemAsync(
        int menuId,
        int? itemId,
        SaveNavigationItemRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.ContentPublish))
        {
            return Forbidden("Editing navigation is not permitted.");
        }

        var menu = await context.NavigationMenus
            .Include(candidate => candidate.Items)
            .FirstOrDefaultAsync(candidate => candidate.Id == menuId, cancellationToken);

        if (menu is null) return NotFound(menuId);

        var externalUrl = Trim(request.ExternalUrl);

        // The same rule the check constraint enforces, stated here so the editor gets a sentence
        // rather than a database error.
        if ((request.PageId is null) == (externalUrl is null))
        {
            return CmsResult<NavigationMenuDetail>.Invalid(
                NavigationCodes.TargetRequired,
                "An entry points at a page or at a URL — one of the two, not both and not neither.",
                nameof(SaveNavigationItemRequest.PageId));
        }

        if (request.PageId is { } pageId &&
            !await context.Pages.AnyAsync(page => page.Id == pageId, cancellationToken))
        {
            return CmsResult<NavigationMenuDetail>.Invalid(
                NavigationCodes.PageNotFound,
                $"Page {pageId} does not exist.",
                nameof(SaveNavigationItemRequest.PageId));
        }

        if (request.ParentId is { } parentId &&
            (parentId == itemId || menu.Items.All(item => item.Id != parentId)))
        {
            return CmsResult<NavigationMenuDetail>.Invalid(
                NavigationCodes.InvalidParent,
                "An entry can only be nested under another entry of the same menu.",
                nameof(SaveNavigationItemRequest.ParentId));
        }

        NavigationItem item;

        if (itemId is { } id)
        {
            if (menu.Items.FirstOrDefault(candidate => candidate.Id == id) is not { } existing)
            {
                return CmsResult<NavigationMenuDetail>.NotFound(
                    $"No item has id {id} in this menu.",
                    NavigationCodes.NotFound);
            }

            item = existing;
        }
        else
        {
            item = new NavigationItem { NavigationMenuId = menu.Id };
            context.NavigationItems.Add(item);
        }

        item.ParentId = request.ParentId;
        item.Label = request.Label.Trim();
        item.PageId = request.PageId;
        item.ExternalUrl = externalUrl;
        item.OpenInNewTab = request.OpenInNewTab;
        item.SortOrder = request.SortOrder;

        cacheInvalidation.Enqueue([CacheTags.Navigation(menu.Key)]);

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<NavigationMenuDetail>.Success((await DetailAsync(menuId, cancellationToken))!);
    }

    private async Task<NavigationMenuDetail?> DetailAsync(int id, CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();

        return await context.NavigationMenus
            .AsNoTracking()
            .Where(menu => menu.Id == id)
            .Select(menu => new NavigationMenuDetail(
                menu.Id,
                menu.Key,
                menu.Name,
                menu.Description,
                menu.Items
                    .OrderBy(item => item.SortOrder)
                    .ThenBy(item => item.Id)
                    .Select(item => new NavigationItemDetail(
                        item.Id,
                        item.ParentId,
                        item.Label,
                        item.PageId,
                        item.Page == null ? null : item.Page.PublishedVersion!.Title,
                        context.PageRoutes
                            .Where(route =>
                                route.PageId == item.PageId &&
                                route.IsPublished &&
                                route.IsPrimary)
                            .Select(route => route.Url)
                            .FirstOrDefault(),
                        item.ExternalUrl,
                        item.OpenInNewTab,
                        item.SortOrder))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static CmsResult<NavigationMenuDetail> NotFound(int id) =>
        CmsResult<NavigationMenuDetail>.NotFound(
            $"No navigation menu has id {id}.",
            NavigationCodes.NotFound);

    private static CmsResult<NavigationMenuDetail> Forbidden(string message) =>
        CmsResult<NavigationMenuDetail>.Forbidden(message, NavigationCodes.Forbidden);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
