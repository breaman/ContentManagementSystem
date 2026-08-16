using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Answers "where is this used?" and "what would change if I published this?" over the
/// <c>ContentReference</c> table (task P4-07, spec section 9.4).
/// </summary>
/// <remarks>
/// The read side of the projection <see cref="IContentReferenceProjector"/> writes. Everything it
/// reports is an indexed query against <c>(TargetType, TargetId)</c> rather than a scan over content
/// payloads, which is the entire reason those rows exist (spec section 6.2).
/// <para>
/// <strong>It answers about the whole graph, not one edge.</strong> Reusable content nests — a
/// banner inside a footer that sits on every page — so a walk that stopped at direct placements
/// would tell an editor that changing a site-wide banner affects nothing, and the confirmation
/// dialog spec section 9.4 requires would never appear for exactly the change that most needs it.
/// </para>
/// <para>
/// The service reads and never writes, and it is deliberately not authorized: it is called from
/// inside operations that have already authorized the caller — a publish, a delete guard — and a
/// second permission check here would mean a publish could succeed while the impact list it is
/// required to record came back empty.
/// </para>
/// </remarks>
public interface IReferenceQueryService
{
    /// <summary>
    /// Finds everything that would be affected by a change to one entity.
    /// </summary>
    /// <param name="targetType">Kind of entity being asked about.</param>
    /// <param name="targetId">Identity of that entity.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>
    /// The affected pages and reusable items, with exact counts. Never null — an entity nothing
    /// points at answers with <see cref="ReferenceImpact.None"/>, which is a fact rather than an
    /// absence of one.
    /// </returns>
    Task<ReferenceImpact> WhereUsedAsync(
        ContentReferenceTargetType targetType,
        int targetId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IReferenceQueryService" />
/// <param name="context">The application database context.</param>
public sealed class ReferenceQueryService(ApplicationDbContext context) : IReferenceQueryService
{
    /// <summary>
    /// How many levels of reusable-content nesting the walk will follow.
    /// </summary>
    /// <remarks>
    /// The same ceiling <c>IReusableContentResolver</c> renders to, and it has to be: an impact
    /// report that counted a page the renderer will refuse to reach would promise a change that
    /// never arrives. Cycles are refused at write time, so this bounds a graph that is already
    /// acyclic — it is the guard against a payload that was hand-edited or restored from a backup
    /// older than the guard, not against ordinary content.
    /// </remarks>
    public const int MaxDepth = 5;

    /// <summary>
    /// How many affected pages are listed before the list is truncated.
    /// </summary>
    /// <remarks>
    /// The counts stay exact whatever this is. A site-wide footer is referenced by every page on the
    /// site, and returning all of them would make a confirmation dialog into a download — while the
    /// number the dialog actually shows comes from the count, not from the list's length.
    /// </remarks>
    public const int MaxListedPages = 100;

    /// <summary>
    /// How many affected pages make a change worth warning about (spec section 9.4).
    /// </summary>
    public const int BlastRadiusThreshold = 10;

    /// <inheritdoc />
    public async Task<ReferenceImpact> WhereUsedAsync(
        ContentReferenceTargetType targetType,
        int targetId,
        CancellationToken cancellationToken = default)
    {
        // Reached by a direct edge, versus reached through a reusable item that itself holds the
        // entity. The two are kept apart for one reason: a pin protects the edge it sits on and
        // nothing beneath it. A page pinning a footer to version 3 does not change when the footer
        // changes — but version 3 of that footer still places this banner late-bound, so the page
        // does change when the banner does. Treating a transitive arrival as unpinned is what makes
        // that page appear in the count it belongs in.
        var pages = new Dictionary<int, PageEdge>();
        var items = new Dictionary<int, AffectedReusableItem>();

        var frontier = new List<(ContentReferenceTargetType Type, int Id)> { (targetType, targetId) };
        var visited = new HashSet<int>();

        for (var depth = 0; depth < MaxDepth && frontier.Count > 0; depth++)
        {
            var edges = await EdgesIntoAsync(frontier, cancellationToken);

            await CollectPagesAsync(edges, isDirectLevel: depth == 0, pages, cancellationToken);

            var next = await CollectItemsAsync(edges, items, visited, cancellationToken);

            frontier = [.. next.Select(id => (ContentReferenceTargetType.ReusableContent, id))];
        }

        return Summarize(pages, items);
    }

    /// <summary>Every reference row pointing at any entity in the frontier.</summary>
    private async Task<List<ReferenceEdge>> EdgesIntoAsync(
        IReadOnlyList<(ContentReferenceTargetType Type, int Id)> frontier,
        CancellationToken cancellationToken)
    {
        // One query per target type in the frontier rather than one per entity. After the first
        // level every entry is a reusable item, so this is a single IN clause against the
        // (TargetType, TargetId) index however wide the graph turns out to be.
        var edges = new List<ReferenceEdge>();

        foreach (var group in frontier.GroupBy(entry => entry.Type))
        {
            var ids = group.Select(entry => entry.Id).ToList();

            edges.AddRange(await context.ContentReferences
                .AsNoTracking()
                .Where(row => row.TargetType == group.Key && ids.Contains(row.TargetId))
                .Select(row => new ReferenceEdge(
                    row.SourceType,
                    row.SourceVersionId,
                    row.IsPinned,
                    row.ZoneKey,
                    row.PropertyKey))
                .ToListAsync(cancellationToken));
        }

        return edges;
    }

    /// <summary>Resolves page-version sources to the pages that hold them.</summary>
    private async Task CollectPagesAsync(
        List<ReferenceEdge> edges,
        bool isDirectLevel,
        Dictionary<int, PageEdge> pages,
        CancellationToken cancellationToken)
    {
        var versionIds = edges
            .Where(edge => edge.SourceType is ContentSourceType.PageVersion)
            .Select(edge => edge.SourceVersionId)
            .Distinct()
            .ToList();

        if (versionIds.Count == 0) return;

        // Two filters, and both narrow the answer for the same reason: an impact count an editor is
        // asked to confirm must be the truth and not an upper bound.
        //
        // The query filter on Page stays in place, so a page in the recycle bin is not a page
        // anybody is about to see change. And only a page's *live* versions count — its published
        // one and its draft. An archived version holds reference rows describing content nobody is
        // serving; worse, a page that used to place an item late-bound and now pins it has rows for
        // both, and counting the archived one would report the pinned page as changing.
        var rows = await context.PageVersions
            .AsNoTracking()
            .Where(version => versionIds.Contains(version.Id) &&
                (version.Page.PublishedVersionId == version.Id ||
                    version.Page.DraftVersionId == version.Id))
            .Select(version => new
            {
                VersionId = version.Id,
                version.PageId,
                version.Title,
                IsPublished = version.Page.PublishedVersionId == version.Id,
                IsPageLive = !version.Page.IsDeleted,
                Url = context.PageRoutes
                    .Where(route => route.PageId == version.PageId && route.IsPublished)
                    .Select(route => route.Url)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var byVersion = rows.Where(row => row.IsPageLive).ToDictionary(row => row.VersionId);

        foreach (var edge in edges.Where(edge => edge.SourceType is ContentSourceType.PageVersion))
        {
            if (!byVersion.TryGetValue(edge.SourceVersionId, out var row)) continue;

            // A pin protects the edge it sits on and nothing beneath it. Reaching the page through
            // a reusable item means something below any pin still moves, so a transitive arrival is
            // recorded as unpinned and, merged below, overrides a pin found at the direct level.
            var candidate = new PageEdge(
                row.PageId,
                row.Title,
                row.Url,
                row.IsPublished,
                IsPinned: isDirectLevel && edge.IsPinned,
                isDirectLevel ? edge.ZoneKey : null,
                isDirectLevel ? edge.PropertyKey : null);

            // A page may place the same item twice — pinned in one zone, late-bound in another — and
            // it changes either way, so the unpinned edge wins. IsPinned survives only when every
            // edge reaching this page is pinned.
            pages[row.PageId] = pages.TryGetValue(row.PageId, out var existing)
                ? existing with
                {
                    IsPinned = existing.IsPinned && candidate.IsPinned,
                    IsPublished = existing.IsPublished || candidate.IsPublished,
                    ZoneKey = existing.ZoneKey ?? candidate.ZoneKey,
                    PropertyKey = existing.PropertyKey ?? candidate.PropertyKey,
                }
                : candidate;
        }
    }

    /// <summary>
    /// Resolves reusable-version sources to their items, and reports the ones to walk into next.
    /// </summary>
    private async Task<List<int>> CollectItemsAsync(
        List<ReferenceEdge> edges,
        Dictionary<int, AffectedReusableItem> items,
        HashSet<int> visited,
        CancellationToken cancellationToken)
    {
        var versionIds = edges
            .Where(edge => edge.SourceType is ContentSourceType.ReusableContentVersion)
            .Select(edge => edge.SourceVersionId)
            .Distinct()
            .ToList();

        if (versionIds.Count == 0) return [];

        // Live versions only, exactly as for pages above: an archived version's placements describe
        // a snapshot nobody renders and nobody can repair.
        var rows = await context.ReusableContentVersions
            .AsNoTracking()
            .Where(version => versionIds.Contains(version.Id) &&
                (version.ReusableContent.PublishedVersionId == version.Id ||
                    version.ReusableContent.DraftVersionId == version.Id))
            .Select(version => new
            {
                version.ReusableContentId,
                version.ReusableContent.Key,
                version.ReusableContent.Name,
                IsPublished = version.ReusableContent.PublishedVersionId == version.Id,
            })
            .ToListAsync(cancellationToken);

        var next = new List<int>();

        foreach (var row in rows)
        {
            // Published wins over draft when an item holds the reference in both: the published
            // placement is the one that reaches a visitor, and it is what decides whether the pages
            // below this item are live pages or drafts.
            items[row.ReusableContentId] = items.TryGetValue(row.ReusableContentId, out var existing)
                ? existing with { IsPublished = existing.IsPublished || row.IsPublished }
                : new AffectedReusableItem(row.ReusableContentId, row.Key, row.Name, row.IsPublished);

            if (visited.Add(row.ReusableContentId)) next.Add(row.ReusableContentId);
        }

        return next;
    }

    /// <summary>Turns the collected edges into the shape spec section 9.4 promises.</summary>
    private static ReferenceImpact Summarize(
        Dictionary<int, PageEdge> pages,
        Dictionary<int, AffectedReusableItem> items)
    {
        var affected = new List<AffectedPage>(pages.Count);
        var changing = 0;
        var pinned = 0;

        foreach (var edge in pages.Values)
        {
            affected.Add(new AffectedPage(
                edge.PageId,
                edge.Title,
                edge.Url,
                edge.IsPublished,
                edge.IsPinned,
                edge.ZoneKey,
                edge.PropertyKey));

            // Only published pages are counted. A draft holding the reference is listed — deleting
            // the target still breaks it — but it is not a page a visitor is about to see change,
            // and counting it would inflate the number the confirmation dialog asks somebody to
            // accept responsibility for.
            if (!edge.IsPublished) continue;

            if (edge.IsPinned) pinned++;
            else changing++;
        }

        var warnings = changing >= BlastRadiusThreshold
            ? new[]
            {
                new ApiDiagnostic(
                    ReusableCodes.BlastRadius,
                    $"{changing} published pages will change immediately."),
            }
            : [];

        var ordered = affected.OrderBy(page => page.Id).ToList();

        return new ReferenceImpact(
            ordered.Count > MaxListedPages ? ordered[..MaxListedPages] : ordered,
            changing,
            pinned,
            [.. items.Values.OrderBy(item => item.Id)],
            warnings,
            ordered.Count > MaxListedPages);
    }

    /// <summary>One <c>ContentReference</c> row, reduced to what the walk reads.</summary>
    private sealed record ReferenceEdge(
        ContentSourceType SourceType,
        int SourceVersionId,
        bool IsPinned,
        string? ZoneKey,
        string? PropertyKey);

    /// <summary>One page's accumulated connection to the entity, across every edge that reaches it.</summary>
    private sealed record PageEdge(
        int PageId,
        string Title,
        string? Url,
        bool IsPublished,
        bool IsPinned,
        string? ZoneKey,
        string? PropertyKey);
}
