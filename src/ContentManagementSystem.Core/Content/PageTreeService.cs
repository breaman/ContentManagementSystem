using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Content;

/// <inheritdoc cref="IPageTreeService" />
/// <param name="context">The application database context.</param>
public sealed class PageTreeService(ApplicationDbContext context) : IPageTreeService
{
    /// <inheritdoc />
    public async Task AttachAsync(Page page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (page.Id == 0)
        {
            throw new ArgumentException(
                "A page's path contains its own id, so it can only be built after the row is inserted.",
                nameof(page));
        }

        var parent = await LoadParentAsync(page.ParentId, cancellationToken);

        page.Path = IPageTreeService.BuildPath(parent?.Path, page.Id);
        page.Depth = parent is null ? 0 : parent.Depth + 1;
    }

    /// <inheritdoc />
    public async Task<PageMoveOutcome> MoveAsync(
        Page page,
        int? newParentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (string.IsNullOrEmpty(page.Path))
        {
            throw new ArgumentException(
                $"The page has no path, so it was never attached to the tree. Call {nameof(AttachAsync)} first.",
                nameof(page));
        }

        if (newParentId == page.Id) return PageMoveOutcome.WouldCreateCycle;

        var parent = await LoadParentAsync(newParentId, cancellationToken);
        if (newParentId is not null && parent is null) return PageMoveOutcome.ParentNotFound;

        // A parent whose own path runs through this page is a descendant of it. Moving a subtree
        // inside itself would leave it referenced by nothing reachable from the root, and no query
        // would report it as missing — the tree would simply be shorter than the table.
        if (parent is not null && parent.Path.StartsWith(page.Path, StringComparison.Ordinal))
        {
            return PageMoveOutcome.WouldCreateCycle;
        }

        // Deleted descendants move too. Their paths have to stay correct or restoring one later
        // puts it back into a branch that no longer exists (spec section 14.10).
        var descendants = await context.Pages
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id != page.Id && candidate.Path.StartsWith(page.Path))
            .ToListAsync(cancellationToken);

        var newDepth = parent is null ? 0 : parent.Depth + 1;
        var deepest = descendants.Count == 0 ? page.Depth : descendants.Max(d => d.Depth);
        if (newDepth + (deepest - page.Depth) > IPageTreeService.MaxDepth) return PageMoveOutcome.TooDeep;

        var oldPath = page.Path;
        var depthShift = newDepth - page.Depth;

        page.ParentId = parent?.Id;
        page.Path = IPageTreeService.BuildPath(parent?.Path, page.Id);
        page.Depth = newDepth;

        foreach (var descendant in descendants)
        {
            // Only the ancestry prefix changes; everything below this page keeps its shape, which
            // is the whole reason the path is stored rather than recomputed from ParentId.
            descendant.Path = string.Concat(page.Path, descendant.Path.AsSpan(oldPath.Length));
            descendant.Depth += depthShift;
        }

        return PageMoveOutcome.Moved;
    }

    /// <summary>
    /// Loads a prospective parent, or null when the page belongs at the root.
    /// </summary>
    /// <remarks>
    /// Tracked rather than <c>AsNoTracking</c>: the caller may already have the parent loaded, and
    /// handing back a second instance of the same row is how a change to one of them quietly loses.
    /// The query filter is left in place, so a deleted page cannot be adopted as a parent — that
    /// would resurrect its subtree into the live tree without restoring it.
    /// </remarks>
    private async Task<Page?> LoadParentAsync(int? parentId, CancellationToken cancellationToken) =>
        parentId is null
            ? null
            : await context.Pages.FirstOrDefaultAsync(
                candidate => candidate.Id == parentId,
                cancellationToken);
}
