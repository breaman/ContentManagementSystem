using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Soft delete, restore, and the one irreversible operation in the system
/// (task P2-08, spec section 14.10).
/// </summary>
/// <remarks>
/// Soft delete is the only delete most roles have. A deleted page keeps its version history, its
/// reference rows, and its audit trail; what changes is that ordinary queries stop seeing it and it
/// stops being served.
/// <para>
/// Everything here is subtree-aware. Deleting a section deletes what is under it — a live child
/// under a deleted parent is a page reachable by URL and invisible in the tree — and restoring one
/// brings the same set back, as drafts.
/// </para>
/// </remarks>
public interface IRecycleBinService
{
    /// <summary>Lists what is in the bin, most recently deleted first.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The entries, or a forbidden result.</returns>
    Task<CmsResult<IReadOnlyList<RecycleBinEntry>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports what deleting a page would take with it, without deleting anything (task P6-04).
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The subtree's size and how much of it is live, or a not-found result.</returns>
    /// <remarks>
    /// Exists so a confirmation can state the consequence before somebody agrees to it: one delete
    /// takes a whole section with it, and "delete this page?" is not the question being asked when
    /// the page has forty descendants (acceptance criterion P6 #10).
    /// </remarks>
    Task<CmsResult<SubtreeImpact>> DescribeAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a page and everything beneath it.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What was affected, or a not-found result.</returns>
    Task<CmsResult<SubtreeResult>> DeleteAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a page and everything that was deleted with it.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What came back, with a warning if it had to come back at the root.</returns>
    /// <remarks>
    /// <strong>A restored page comes back as a draft.</strong> It does not silently reappear on the
    /// public site — somebody publishes it again deliberately, having looked at it.
    /// </remarks>
    Task<CmsResult<SubtreeResult>> RestoreAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a page and its history from the database for good.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    /// <returns>
    /// The number of rows removed, or a conflict naming the pages whose content still points here.
    /// </returns>
    /// <remarks>
    /// Refused while any <c>ContentReference</c> row targets the page, and refused unless the page
    /// is already in the bin. The guard is a query rather than a constraint, because both ends of
    /// that table are polymorphic — which is also why it over-reports, and why this refusal is
    /// occasionally more cautious than it strictly needs to be.
    /// </remarks>
    Task<CmsResult<int>> PurgeAsync(int pageId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IRecycleBinService" />
/// <param name="context">The application database context.</param>
/// <param name="urls">Withdraws the public routes a delete retires, and refreshes them on restore.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (task P7-06, spec section 21.2).</param>
/// <param name="logger">Log for every delete, restore, and purge.</param>
public sealed class RecycleBinService(
    ApplicationDbContext context,
    IUrlService urls,
    ICmsAuthorization authorization,
    IAclService acl,
    ILogger<RecycleBinService> logger) : IRecycleBinService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<RecycleBinEntry>>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentDelete))
        {
            return CmsResult<IReadOnlyList<RecycleBinEntry>>.Forbidden(
                "Managing deleted content is not permitted.",
                PageCodes.Forbidden);
        }

        var deleted = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(page => page.IsDeleted)
            .Include(page => page.DraftVersion)
            .ToListAsync(cancellationToken);

        // The bin is a view of the tree, so it inherits the tree's access rules. An editor confined
        // to /products sees what they deleted and nothing else — otherwise the bin would be the one
        // screen in the backoffice that lists the whole site.
        var deletable = await acl.GetFilterAsync(CmsPermissions.ContentDelete, cancellationToken);

        if (!deletable.IsUnrestricted)
        {
            deleted = [.. deleted.Where(page => deletable.Allows(page.Id, page.Path))];
        }

        var entries = new List<RecycleBinEntry>(deleted.Count);

        foreach (var page in deleted)
        {
            // A page whose deleted parent is also in the bin was swept up by that parent's delete.
            // Reporting it as a root as well would turn one delete of a section into a dozen entries
            // an editor has to restore one at a time.
            var isRoot = !deleted.Any(other =>
                other.Id != page.Id && page.Path.StartsWith(other.Path, StringComparison.Ordinal));

            entries.Add(new RecycleBinEntry(
                page.Id,
                page.DraftVersion?.Title ?? page.Slug,
                page.Slug,
                page.ParentId,
                isRoot,
                deleted.Count(other => other.Id != page.Id &&
                    other.Path.StartsWith(page.Path, StringComparison.Ordinal)),
                page.PublishedVersionId is not null,
                page.DeletedOn,
                page.DeletedBy));
        }

        return CmsResult<IReadOnlyList<RecycleBinEntry>>.Success(entries
            .OrderByDescending(entry => entry.DeletedOn)
            .ThenBy(entry => entry.Id)
            .ToList());
    }

    /// <inheritdoc />
    public async Task<CmsResult<SubtreeImpact>> DescribeAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentDelete))
        {
            return CmsResult<SubtreeImpact>.Forbidden(
                "Deleting pages is not permitted.",
                PageCodes.Forbidden);
        }

        var page = await context.Pages
            .AsNoTracking()
            .Include(candidate => candidate.DraftVersion)
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null || !await acl.IsAllowedAsync(CmsPermissions.ContentDelete, pageId, cancellationToken))
        {
            return CmsResult<SubtreeImpact>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        // One indexed prefix match over the materialized path, which is the same query the delete
        // itself walks — so the number shown and the number affected come from the same definition
        // of "beneath this page" (spec section 10.1).
        var descendants = await context.Pages
            .AsNoTracking()
            .Where(candidate => candidate.Id != page.Id && candidate.Path.StartsWith(page.Path))
            .Select(candidate => candidate.PublishedVersionId)
            .ToListAsync(cancellationToken);

        var published = descendants.Count(version => version is not null) +
            (page.PublishedVersionId is null ? 0 : 1);

        return CmsResult<SubtreeImpact>.Success(new SubtreeImpact(
            page.Id,
            page.DraftVersion?.Title ?? $"Page {page.Id}",
            descendants.Count,
            published));
    }

    /// <inheritdoc />
    public async Task<CmsResult<SubtreeResult>> DeleteAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentDelete))
        {
            return Forbidden("Deleting pages is not permitted.");
        }

        var page = await context.Pages.FirstOrDefaultAsync(
            candidate => candidate.Id == pageId,
            cancellationToken);

        if (page is null) return NotFound(pageId);

        // Only the root of the delete is checked, and that is deliberate: a delete takes the whole
        // subtree with it, and a rule can only narrow access further down, never widen it. A caller
        // allowed to delete this page is therefore allowed to delete everything beneath it.
        if (!await acl.IsAllowedAsync(CmsPermissions.ContentDelete, pageId, cancellationToken))
        {
            return Forbidden($"Deleting page {pageId} is not permitted.");
        }

        var subtree = await LoadSubtreeAsync(page, includeDeleted: false, cancellationToken);
        var unpublished = 0;

        foreach (var node in subtree)
        {
            // Retiring the published pointer is what takes the page off the public site. The version
            // itself is archived rather than removed — history is precisely what a soft delete
            // exists to preserve, and a restore has to have something to show.
            if (node.PublishedVersionId is not null)
            {
                unpublished++;
                node.PublishedVersionId = null;
            }

            // Assigned rather than removed through the set: SoftDeleteInterceptor would rewrite a Remove
            // into exactly this, and going through it directly keeps the intent visible at the call
            // site instead of relying on a hook to reinterpret it.
            node.IsDeleted = true;
        }

        // The published routes go with the pages, in the same save. The draft routes stay, because
        // the recycle bin's whole promise is that this is reversible and a restore must not have to
        // rebuild a page's explicit URL from nothing.
        var withdrawn = await urls.WithdrawAsync(pageId, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Page {PageId} and {DescendantCount} descendants were moved to the recycle bin; " +
            "{UnpublishedCount} were live and {UrlCount} URL(s) were withdrawn.",
            pageId,
            subtree.Count - 1,
            unpublished,
            withdrawn.Count);

        // No redirect is left behind. Deleting is not moving, and a redirect to the parent would be
        // this service inventing an editorial decision — spec section 10.6 puts the vacated URL in
        // the NotFoundLog report instead, where somebody can see whether it still receives traffic.
        return CmsResult<SubtreeResult>.Success(new SubtreeResult(
            pageId,
            subtree.Select(node => node.Id).ToList(),
            unpublished,
            []));
    }

    /// <inheritdoc />
    public async Task<CmsResult<SubtreeResult>> RestoreAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentDelete))
        {
            return Forbidden("Restoring pages is not permitted.");
        }

        var page = await context.Pages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null) return NotFound(pageId);

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentDelete, pageId, cancellationToken))
        {
            return Forbidden($"Restoring page {pageId} is not permitted.");
        }

        if (!page.IsDeleted)
        {
            return CmsResult<SubtreeResult>.Invalid(
                PageCodes.PageNotDeleted,
                "This page is not in the recycle bin.");
        }

        var warnings = new List<ValidationDiagnostic>();

        // A parent still in the bin would leave the restored page in a branch nothing can reach, so
        // it comes back at the root with a warning rather than not coming back at all
        // (spec section 14.10).
        if (page.ParentId is { } parentId)
        {
            var parentIsDeleted = await context.Pages
                .IgnoreQueryFilters()
                .AnyAsync(candidate => candidate.Id == parentId && candidate.IsDeleted, cancellationToken);

            if (parentIsDeleted)
            {
                warnings.Add(new ValidationDiagnostic(
                    PageCodes.ParentStillDeleted,
                    $"The former parent of this page is still in the recycle bin, so it has been " +
                    "restored at the root of the site. Move it once the parent is back.",
                    ValidationSeverity.Warning));
            }
        }

        var subtree = await LoadSubtreeAsync(page, includeDeleted: true, cancellationToken);
        var restored = new List<int>();

        foreach (var node in subtree)
        {
            if (!node.IsDeleted) continue;

            node.IsDeleted = false;
            node.DeletedOn = null;
            node.DeletedBy = null;

            // Deliberately left null. A restored page comes back as a draft; anything else would put
            // content back on the public site that nobody has looked at since it was deleted.
            node.PublishedVersionId = null;

            restored.Add(node.Id);
        }

        if (warnings.Count > 0)
        {
            page.ParentId = null;
            page.Path = IPageTreeService.BuildPath(null, page.Id);
            page.Depth = 0;

            RepositionDescendants(page, subtree);
        }

        // Draft routes are refreshed because a page restored at the root has a different URL from
        // the one it had under its old parent, and a stale draft route is a preview link that opens
        // somebody else's page. No published route is written: every restored page came back as a
        // draft two blocks above.
        await urls.SyncAsync(pageId, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Page {PageId} and {DescendantCount} descendants were restored as drafts.",
            pageId,
            restored.Count - 1);

        return CmsResult<SubtreeResult>.Success(new SubtreeResult(
            pageId,
            restored,
            0,
            ApiDiagnostics.Project(ValidationResult.From(warnings), ValidationSeverity.Warning)));
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> PurgeAsync(int pageId, CancellationToken cancellationToken = default)
    {
        // Administrator-only, and separate from ContentDelete on purpose: this is the one operation
        // in the system with no undo (spec section 14.10).
        if (!authorization.HasPermission(CmsPermissions.UsersManage))
        {
            return CmsResult<int>.Forbidden(
                "Permanently deleting content is not permitted.",
                PageCodes.Forbidden);
        }

        var page = await context.Pages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null) return CmsResult<int>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);

        if (!page.IsDeleted)
        {
            return CmsResult<int>.Invalid(
                PageCodes.PageNotDeleted,
                "A page has to be in the recycle bin before it can be permanently deleted.");
        }

        var children = await context.Pages
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.ParentId == pageId, cancellationToken);

        if (children)
        {
            return CmsResult<int>.Invalid(
                PageCodes.StillReferenced,
                "This page still has descendants in the recycle bin. Permanently delete those first.");
        }

        var referencing = await ReferencingPageIdsAsync(pageId, cancellationToken);

        if (referencing.Count > 0)
        {
            return CmsResult<int>.Conflict(
                PageCodes.StillReferenced,
                $"Stored content on {referencing.Count} other " +
                $"page{(referencing.Count == 1 ? string.Empty : "s")} still links to this one: " +
                $"{string.Join(", ", referencing)}. Replace or remove those links first.");
        }

        var versionIds = await context.PageVersions
            .Where(version => version.PageId == pageId)
            .Select(version => version.Id)
            .ToListAsync(cancellationToken);

        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();

            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // The pointers have to go first: Page and PageVersion reference each other, and both
            // directions are Restrict, so nothing can be deleted while either pointer is set.
            await context.Pages
                .IgnoreQueryFilters()
                .Where(candidate => candidate.Id == pageId)
                .ExecuteUpdateAsync(
                    updates => updates
                        .SetProperty(candidate => candidate.DraftVersionId, (int?)null)
                        .SetProperty(candidate => candidate.PublishedVersionId, (int?)null),
                    cancellationToken);

            await context.ContentReferences
                .Where(row => row.SourceType == ContentSourceType.PageVersion &&
                    versionIds.Contains(row.SourceVersionId))
                .ExecuteDeleteAsync(cancellationToken);

            await context.EditLocks
                .Where(row => row.PageId == pageId)
                .ExecuteDeleteAsync(cancellationToken);

            // A preview token names a version by foreign key, and that one is Restrict so version
            // retention cannot silently strand a shared link. Here the versions really are going, so
            // the tokens go with them — a link pointing at a purged page has nothing left to show.
            await context.PreviewTokens
                .Where(token => token.PageId == pageId)
                .ExecuteDeleteAsync(cancellationToken);

            // Redirects pointing at this page are rewritten to the URL it last served rather than
            // deleted, which is the whole reason Redirect.ToPageId is Restrict. Deleting them would
            // silently drop rules an administrator entered, and leaving them would fail the purge on
            // a foreign key with no explanation an operator could act on.
            await RewriteInboundRedirectsAsync(pageId, cancellationToken);

            await context.PageRoutes
                .Where(route => route.PageId == pageId)
                .ExecuteDeleteAsync(cancellationToken);

            var versions = await context.PageVersions
                .Where(version => version.PageId == pageId)
                .ExecuteDeleteAsync(cancellationToken);

            var pages = await context.Pages
                .IgnoreQueryFilters()
                .Where(candidate => candidate.Id == pageId)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            logger.LogWarning(
                "Page {PageId} was permanently deleted with {VersionCount} versions. This is not reversible.",
                pageId,
                versions);

            return CmsResult<int>.Success(versions + pages);
        });
    }

    /// <summary>
    /// Points every redirect that targets a page at the URL that page last served.
    /// </summary>
    /// <remarks>
    /// Run inside the purge transaction, before the routes are deleted, because the URL being
    /// preserved is read out of those very rows. A redirect whose target had no route at all is
    /// removed: it was pointing at nothing already, and keeping it would occupy its source URL
    /// against a rule somebody might want to write later.
    /// </remarks>
    private async Task RewriteInboundRedirectsAsync(int pageId, CancellationToken cancellationToken)
    {
        var lastUrl = await context.PageRoutes
            .AsNoTracking()
            .Where(route => route.PageId == pageId)
            .OrderByDescending(route => route.IsPublished)
            .ThenByDescending(route => route.IsPrimary)
            .Select(route => route.Url)
            .FirstOrDefaultAsync(cancellationToken);

        if (lastUrl is null)
        {
            await context.Redirects
                .Where(redirect => redirect.ToPageId == pageId)
                .ExecuteDeleteAsync(cancellationToken);

            return;
        }

        await context.Redirects
            .Where(redirect => redirect.ToPageId == pageId)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(redirect => redirect.ToPageId, (int?)null)
                    .SetProperty(redirect => redirect.ToUrl, lastUrl),
                cancellationToken);
    }

    /// <summary>
    /// Loads a page and its descendants by materialized path.
    /// </summary>
    /// <param name="page">The subtree root, already tracked.</param>
    /// <param name="includeDeleted">
    /// Whether deleted descendants are included. A delete only touches live ones; a restore has to
    /// see the deleted ones, since that is all there is.
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The root first, then its descendants.</returns>
    private async Task<List<Page>> LoadSubtreeAsync(
        Page page,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var query = context.Pages.IgnoreQueryFilters()
            .Where(candidate => candidate.Id != page.Id && candidate.Path.StartsWith(page.Path));

        if (!includeDeleted)
        {
            query = query.Where(candidate => !candidate.IsDeleted);
        }

        var descendants = await query
            .OrderBy(candidate => candidate.Depth)
            .ThenBy(candidate => candidate.SortOrder)
            .ToListAsync(cancellationToken);

        return [page, .. descendants];
    }

    /// <summary>Rewrites descendant paths after a restore had to move its root to the site root.</summary>
    private static void RepositionDescendants(Page root, IReadOnlyList<Page> subtree)
    {
        foreach (var node in subtree)
        {
            if (node.Id == root.Id) continue;

            var parent = subtree.FirstOrDefault(candidate => candidate.Id == node.ParentId);

            if (parent is null) continue;

            node.Path = IPageTreeService.BuildPath(parent.Path, node.Id);
            node.Depth = parent.Depth + 1;
        }
    }

    /// <summary>Finds the pages whose stored content still links to this one.</summary>
    private async Task<IReadOnlyList<int>> ReferencingPageIdsAsync(
        int pageId,
        CancellationToken cancellationToken)
    {
        var versionIds = await context.ContentReferences
            .AsNoTracking()
            .Where(row => row.TargetType == ContentReferenceTargetType.Page && row.TargetId == pageId)
            .Select(row => row.SourceVersionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (versionIds.Count == 0) return [];

        // The page being purged references itself in its own versions' rows, and those go with it.
        return await context.PageVersions
            .AsNoTracking()
            .Where(version => versionIds.Contains(version.Id) && version.PageId != pageId)
            .Select(version => version.PageId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancellationToken);
    }

    private static CmsResult<SubtreeResult> NotFound(int pageId) =>
        CmsResult<SubtreeResult>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);

    private static CmsResult<SubtreeResult> Forbidden(string message) =>
        CmsResult<SubtreeResult>.Forbidden(message, PageCodes.Forbidden);
}
