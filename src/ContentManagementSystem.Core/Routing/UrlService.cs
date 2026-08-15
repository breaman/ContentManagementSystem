using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Routing;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Routing;

/// <inheritdoc cref="IUrlService" />
/// <param name="context">The application database context.</param>
/// <param name="redirects">Leaves an automatic redirect behind at every published URL vacated.</param>
/// <param name="clock">Source of the current time, stamped on materialized routes.</param>
/// <param name="logger">Log for subtree rebuilds and refused collisions.</param>
public sealed class UrlService(
    ApplicationDbContext context,
    IRedirectService redirects,
    TimeProvider clock,
    ILogger<UrlService> logger) : IUrlService
{
    /// <inheritdoc />
    public async Task<string?> ComputeAsync(int pageId, CancellationToken cancellationToken = default)
    {
        var page = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null) return null;

        return IUrlService.Build(page, await ParentUrlAsync(page, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<UrlSyncResult> SyncAsync(int pageId, CancellationToken cancellationToken = default)
    {
        var page = await context.Pages
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null || string.IsNullOrEmpty(page.Path)) return UrlSyncResult.Unchanged;

        // Deleted descendants are included so their draft routes keep pace with the tree. Restoring
        // a page whose ancestors moved while it sat in the recycle bin has to put it back at a URL
        // that reflects where the tree is now, not where it was when it was deleted.
        var subtree = await context.Pages
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == page.Id || candidate.Path.StartsWith(page.Path))
            .OrderBy(candidate => candidate.Depth)
            .ToListAsync(cancellationToken);

        var parentUrl = await ParentUrlAsync(page, cancellationToken);
        var computed = new Dictionary<int, string>(subtree.Count);
        var diagnostics = new List<ValidationDiagnostic>();

        foreach (var node in subtree)
        {
            // Depth order guarantees the parent was computed first, except for the subtree's own
            // root, whose parent sits outside the walk.
            var ancestorUrl = node.Id == page.Id
                ? parentUrl
                : computed.GetValueOrDefault(node.ParentId ?? 0);

            var url = IUrlService.Build(node, ancestorUrl);
            computed[node.Id] = url;

            if (url.Length > FieldLengths.Url)
            {
                diagnostics.Add(new ValidationDiagnostic(
                    RoutingCodes.UrlTooLong,
                    $"The URL of '{node.Slug}' would be {url.Length} characters, over the " +
                    $"{FieldLengths.Url} a URL may be. Shorten a slug above it, or give the page an " +
                    "explicit URL.",
                    ValidationSeverity.Error,
                    $"page[{node.Id}]"));
            }
        }

        var subtreeIds = computed.Keys.ToList();

        var existing = await context.PageRoutes
            .Where(route => subtreeIds.Contains(route.PageId))
            .ToListAsync(cancellationToken);

        diagnostics.AddRange(await FindCollisionsAsync(subtree, computed, subtreeIds, cancellationToken));

        // Nothing has been written yet: every branch above only reads or fills dictionaries. A
        // refusal therefore leaves the caller's transaction exactly as it found it, which is the
        // contract the interface states.
        if (diagnostics.Exists(diagnostic => diagnostic.Severity is ValidationSeverity.Error))
        {
            return new UrlSyncResult([], ValidationResult.From(diagnostics));
        }

        var changes = new List<PageUrlChange>();
        var now = clock.GetUtcNow();

        foreach (var node in subtree)
        {
            var url = computed[node.Id];
            var change = await ApplyAsync(node, url, existing, now, cancellationToken);

            if (change is not null) changes.Add(change);
        }

        if (changes.Count > 0)
        {
            logger.LogInformation(
                "Rebuilt routes below page {PageId}: {ChangeCount} URL(s) moved, {RedirectCount} redirect(s) left behind.",
                page.Id,
                changes.Count,
                changes.Count(change => change.RedirectCreated));
        }

        return new UrlSyncResult(changes, ValidationResult.From(diagnostics));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> WithdrawAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        var page = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null || string.IsNullOrEmpty(page.Path)) return [];

        var published = await context.PageRoutes
            .Where(route =>
                route.IsPublished &&
                (route.PageId == page.Id ||
                 context.Pages
                     .IgnoreQueryFilters()
                     .Any(candidate => candidate.Id == route.PageId && candidate.Path.StartsWith(page.Path))))
            .ToListAsync(cancellationToken);

        var urls = published.ConvertAll(route => route.Url);

        context.PageRoutes.RemoveRange(published);

        return urls;
    }

    /// <inheritdoc />
    public async Task PurgeAsync(int pageId, CancellationToken cancellationToken = default) =>
        await context.PageRoutes
            .Where(route => route.PageId == pageId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Brings one page's route rows into line with the URL it should now have.
    /// </summary>
    /// <returns>What changed, or null when the page was already correct.</returns>
    private async Task<PageUrlChange?> ApplyAsync(
        Page node,
        string url,
        List<PageRoute> existing,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var draft = existing.Find(route => route.PageId == node.Id && !route.IsPublished);
        var live = existing.Find(route => route.PageId == node.Id && route.IsPublished);

        // A recycled page keeps no public URL whatever its version pointers say — the recycle bin
        // is a soft delete, and a soft-deleted page that still answers requests is not deleted.
        var shouldBeLive = node.PublishedVersionId is not null && !node.IsDeleted;
        var oldLiveUrl = live?.Url;

        if (draft is null)
        {
            context.PageRoutes.Add(new PageRoute
            {
                PageId = node.Id,
                Url = url,
                UrlHash = SiteUrls.Hash(url),
                IsPrimary = true,
                IsPublished = false,
                CreatedOn = now,
            });
        }
        else if (!string.Equals(draft.Url, url, StringComparison.Ordinal))
        {
            draft.Url = url;
            draft.UrlHash = SiteUrls.Hash(url);
        }

        if (!shouldBeLive)
        {
            if (live is null) return null;

            context.PageRoutes.Remove(live);

            return new PageUrlChange(node.Id, oldLiveUrl, url, RedirectCreated: false);
        }

        if (live is null)
        {
            context.PageRoutes.Add(new PageRoute
            {
                PageId = node.Id,
                Url = url,
                UrlHash = SiteUrls.Hash(url),
                IsPrimary = true,
                IsPublished = true,
                CreatedOn = now,
            });

            // First publication rather than a move: there is no vacated URL, so no redirect.
            return new PageUrlChange(node.Id, null, url, RedirectCreated: false);
        }

        if (string.Equals(live.Url, url, StringComparison.Ordinal)) return null;

        live.Url = url;
        live.UrlHash = SiteUrls.Hash(url);

        // The redirect is what makes a reorganisation survivable: every inbound link, bookmark, and
        // search result pointing at the old URL keeps working (spec section 10.5).
        var created = await redirects.RecordAutomaticAsync(oldLiveUrl!, node.Id, cancellationToken);

        return new PageUrlChange(node.Id, oldLiveUrl, url, created);
    }

    /// <summary>
    /// Finds published URLs in the rebuilt subtree that another page already serves.
    /// </summary>
    /// <remarks>
    /// Checked rather than left to the filtered unique index for two reasons: a constraint violation
    /// reaches the client as a 500 naming nothing actionable, and a collision <em>inside</em> the
    /// subtree being rebuilt never reaches the database at all — two descendants computing the same
    /// URL would be inserted and updated in one batch that the index would reject wholesale.
    /// </remarks>
    private async Task<List<ValidationDiagnostic>> FindCollisionsAsync(
        List<Page> subtree,
        Dictionary<int, string> computed,
        List<int> subtreeIds,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        var live = subtree
            .Where(node => node.PublishedVersionId is not null && !node.IsDeleted)
            .ToList();

        if (live.Count == 0) return diagnostics;

        var claimed = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var node in live)
        {
            var url = computed[node.Id];

            if (claimed.TryGetValue(url, out var other))
            {
                diagnostics.Add(new ValidationDiagnostic(
                    RoutingCodes.UrlTaken,
                    $"Pages {other} and {node.Id} would both be published at '{url}'.",
                    ValidationSeverity.Error,
                    $"page[{node.Id}]"));

                continue;
            }

            claimed[url] = node.Id;
        }

        var hashes = claimed.Keys.Select(SiteUrls.Hash).ToList();

        var outsiders = await context.PageRoutes
            .AsNoTracking()
            .Where(route =>
                route.IsPublished &&
                !subtreeIds.Contains(route.PageId) &&
                hashes.Contains(route.UrlHash))
            .Select(route => new { route.PageId, route.Url })
            .ToListAsync(cancellationToken);

        foreach (var outsider in outsiders)
        {
            diagnostics.Add(new ValidationDiagnostic(
                RoutingCodes.UrlTaken,
                $"Page {outsider.PageId} is already published at '{outsider.Url}'.",
                ValidationSeverity.Error,
                $"page[{claimed.GetValueOrDefault(outsider.Url)}]"));
        }

        return diagnostics;
    }

    /// <summary>
    /// Computes the URL of a page's parent by walking the materialized path from the root down.
    /// </summary>
    /// <remarks>
    /// The path is used rather than following <c>ParentId</c> one row at a time, so the whole
    /// ancestry is one query however deep the page sits — which is the reason the column exists
    /// (spec section 10.1).
    /// </remarks>
    private async Task<string?> ParentUrlAsync(Page page, CancellationToken cancellationToken)
    {
        if (page.ParentId is null) return null;

        var ancestorIds = ParseAncestorIds(page.Path, page.Id);

        if (ancestorIds.Count == 0) return null;

        var ancestors = await context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(candidate => ancestorIds.Contains(candidate.Id))
            .ToListAsync(cancellationToken);

        var byId = ancestors.ToDictionary(ancestor => ancestor.Id);
        string? url = null;

        foreach (var id in ancestorIds)
        {
            // A missing ancestor means the path disagrees with the table, which PageTreeService is
            // supposed to make impossible. Treating it as the root rather than throwing keeps a
            // publish from failing on data nobody can repair from the editor.
            if (!byId.TryGetValue(id, out var ancestor))
            {
                logger.LogWarning(
                    "Page {PageId} has ancestor {AncestorId} in its path but no such row exists.",
                    page.Id,
                    id);

                continue;
            }

            url = IUrlService.Build(ancestor, url);
        }

        return url;
    }

    /// <summary>Reads the ancestor ids out of a materialized path such as <c>/1/8/44/</c>.</summary>
    /// <remarks>Root first, so the caller can build URLs downward in one pass.</remarks>
    private static List<int> ParseAncestorIds(string path, int selfId)
    {
        var ids = new List<int>();

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(segment, out var id) && id != selfId) ids.Add(id);
        }

        return ids;
    }
}
