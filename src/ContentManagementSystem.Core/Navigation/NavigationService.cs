using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Navigation;

/// <inheritdoc cref="INavigationService" />
/// <param name="context">The application database context.</param>
/// <remarks>
/// Two queries at most, whatever the depth: the tree is read in one pass and assembled in memory,
/// because a menu is a handful of rows and a query per level is the N+1 that only appears once a
/// site has a real tree.
/// <para>
/// No caching here. What renders a menu takes a <c>nav:{menuKey}</c> cache dependency on it, so the
/// menu is cached as part of the page it appears on rather than separately — one cache, one
/// eviction, and no second lifetime to reason about (spec section 16.2).
/// </para>
/// </remarks>
public sealed class NavigationService(ApplicationDbContext context) : INavigationService
{
    /// <summary>Deepest tree the structural menu will build, whatever it is asked for.</summary>
    /// <remarks>
    /// A menu is a menu. Beyond a few levels it is a sitemap, and building one on every page render
    /// is a cost nobody asked for — a template that wants the whole tree should ask for the tree.
    /// </remarks>
    public const int MaxSupportedDepth = 4;

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavigationNode>> GetStructuralAsync(
        int maxDepth = 2,
        int? rootPageId = null,
        CancellationToken cancellationToken = default)
    {
        var depth = Math.Clamp(maxDepth, 1, MaxSupportedDepth);

        var root = rootPageId is > 0
            ? await context.Pages
                .AsNoTracking()
                .Where(page => page.Id == rootPageId)
                .Select(page => new { page.Path, page.Depth })
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (rootPageId is > 0 && root is null) return [];

        var baseDepth = root?.Depth + 1 ?? 0;

        var rows = await context.Pages
            .AsNoTracking()
            .Where(page =>
                page.ShowInNavigation &&
                page.PublishedVersionId != null &&
                page.Depth >= baseDepth &&
                page.Depth < baseDepth + depth &&
                (root == null || (page.Path.StartsWith(root.Path) && page.Id != rootPageId)))
            .OrderBy(page => page.Depth)
            .ThenBy(page => page.SortOrder)
            .Select(page => new NavigationRow(
                page.Id,
                page.ParentId,
                page.PublishedVersion!.Title,
                context.PageRoutes
                    .Where(route => route.PageId == page.Id && route.IsPublished && route.IsPrimary)
                    .Select(route => route.Url)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return Assemble(rows, root is null ? null : rootPageId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NavigationNode>> GetMenuAsync(
        string menuKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(menuKey)) return [];

        var items = await context.NavigationItems
            .AsNoTracking()
            .Where(item => item.Menu.Key == menuKey)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new MenuRow(
                item.Id,
                item.ParentId,
                item.Label,
                item.ExternalUrl,
                item.PageId,
                item.OpenInNewTab,
                item.PageId == null
                    ? null
                    : context.PageRoutes
                        .Where(route =>
                            route.PageId == item.PageId &&
                            route.IsPublished &&
                            route.IsPrimary &&
                            route.Page.PublishedVersionId != null)
                        .Select(route => route.Url)
                        .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        // An internal item whose page is not published resolves to nothing and is dropped, along
        // with anything nested under it. A menu that renders a dead link is worse than a menu with
        // one fewer entry, and it is the failure an editor is least likely to notice.
        var byParent = items
            .Where(item => item.Url is { Length: > 0 } || item.ExternalUrl is { Length: > 0 })
            .ToLookup(item => item.ParentId);

        return Build(byParent, null);
    }

    private static IReadOnlyList<NavigationNode> Build(ILookup<int?, MenuRow> byParent, int? parentId) =>
        [.. byParent[parentId].Select(item => new NavigationNode(
            item.Label,
            item.ExternalUrl ?? item.Url!,
            item.PageId,
            item.OpenInNewTab,
            Build(byParent, item.Id)))];

    /// <summary>Turns the flat page rows into a tree, dropping anything with no reachable URL.</summary>
    private static IReadOnlyList<NavigationNode> Assemble(List<NavigationRow> rows, int? rootPageId)
    {
        var byParent = rows
            .Where(row => row.Url is { Length: > 0 })
            .ToLookup(row => row.ParentId);

        return BuildTree(byParent, rootPageId);
    }

    private static IReadOnlyList<NavigationNode> BuildTree(ILookup<int?, NavigationRow> byParent, int? parentId) =>
        [.. byParent[parentId].Select(row => new NavigationNode(
            row.Title,
            row.Url!,
            row.Id,
            OpenInNewTab: false,
            BuildTree(byParent, row.Id)))];

    /// <summary>One page, as the tree query projects it.</summary>
    private sealed record NavigationRow(int Id, int? ParentId, string Title, string? Url);

    /// <summary>One managed menu item, with its target resolved.</summary>
    private sealed record MenuRow(
        int Id,
        int? ParentId,
        string Label,
        string? ExternalUrl,
        int? PageId,
        bool OpenInNewTab,
        string? Url);
}
