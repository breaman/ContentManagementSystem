using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Routing;

/// <inheritdoc cref="ILinkResolver" />
/// <param name="contexts">Makes a context of its own for each resolve (ADR-0022).</param>
/// <remarks>
/// A context per resolve rather than the request's, because this runs from a field renderer's
/// <c>OnParametersSetAsync</c> and Blazor overlaps sibling renderers' asynchronous lifecycle
/// methods. The read is <c>AsNoTracking</c> and writes nothing, so there is no unit of work that
/// sharing one context would preserve.
/// </remarks>
public sealed class LinkResolver(IDbContextFactory<ApplicationDbContext> contexts) : ILinkResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, ResolvedLink>> ResolveAsync(
        IEnumerable<int> pageIds,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pageIds);

        var ids = pageIds.Distinct().ToList();

        if (ids.Count == 0) return new Dictionary<int, ResolvedLink>();

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        // The query filter is left in place, so a recycled page resolves to nothing and its link
        // degrades to text — which is correct on both surfaces, since neither an editor previewing
        // nor a visitor should be sent to a page in the bin.
        var rows = await context.Pages
            .AsNoTracking()
            .Where(page => ids.Contains(page.Id))
            .Select(page => new
            {
                page.Id,
                IsPublished = page.PublishedVersionId != null,

                // The title comes from the version the audience is looking at: the published one on
                // the public site, the draft in preview. Reading the draft title on a public page
                // would leak an unreleased headline through a link on a page that is otherwise
                // entirely live.
                Title = includeUnpublished
                    ? (page.DraftVersion == null ? null : page.DraftVersion.Title)
                    : (page.PublishedVersion == null ? null : page.PublishedVersion.Title),

                PublishedUrl = context.PageRoutes
                    .Where(route => route.PageId == page.Id && route.IsPublished)
                    .OrderByDescending(route => route.IsPrimary)
                    .Select(route => route.Url)
                    .FirstOrDefault(),

                DraftUrl = context.PageRoutes
                    .Where(route => route.PageId == page.Id && !route.IsPublished)
                    .OrderByDescending(route => route.IsPrimary)
                    .Select(route => route.Url)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var resolved = new Dictionary<int, ResolvedLink>(rows.Count);

        foreach (var row in rows)
        {
            var url = row.IsPublished
                ? row.PublishedUrl
                : includeUnpublished ? row.DraftUrl : null;

            resolved[row.Id] = new ResolvedLink(row.Id, url, row.IsPublished, row.Title);
        }

        return resolved;
    }
}
