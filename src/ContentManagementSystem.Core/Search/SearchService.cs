using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Search;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Search;

/// <inheritdoc cref="ISearchService" />
/// <param name="context">The application database context.</param>
/// <param name="capabilities">Whether this database can answer with the full-text engine.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (spec section 21.2).</param>
/// <param name="clock">Reads today's date, for the review-date filter.</param>
/// <param name="options">Result ceilings and the excerpt length.</param>
/// <remarks>
/// Two ways to match text and one set of filters. The full-text path is what the index was built
/// for; the fallback is a <c>LIKE</c> scan for the deployments with no full-text engine — Azure SQL
/// Edge, and any instance where the catalog has not been built yet. The fallback returns the same
/// rows, and the results say which path answered so that "search is slow" is a visible fact rather
/// than a guess.
/// </remarks>
public sealed class SearchService(
    ApplicationDbContext context,
    SearchCapabilities capabilities,
    ICmsAuthorization authorization,
    IAclService acl,
    TimeProvider clock,
    IOptions<SearchOptions> options) : ISearchService
{
    /// <summary>Escape character used with <c>LIKE</c>, so a search term's wildcards are literal.</summary>
    private const string LikeEscape = "\\";

    /// <inheritdoc />
    public async Task<CmsResult<SearchResults>> SearchAsync(
        SearchQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<SearchResults>.Forbidden(
                "Searching content is not permitted.",
                SearchCodes.Forbidden);
        }

        PageVersionStatus? status = null;

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            // Refused rather than ignored, as the page list does it: a mistyped status that quietly
            // matched everything reads as "every page is a draft".
            if (!Enum.TryParse(query.Status, ignoreCase: true, out PageVersionStatus parsed))
            {
                return CmsResult<SearchResults>.Invalid(
                    SearchCodes.UnknownStatus,
                    $"'{query.Status}' is not a page status. Try one of: " +
                    $"{string.Join(", ", Enum.GetNames<PageVersionStatus>())}.",
                    nameof(SearchQuery.Status));
            }

            status = parsed;
        }

        var settings = options.Value;
        var limit = Math.Clamp(query.Limit ?? settings.DefaultResults, 1, Math.Max(1, settings.MaxResults));
        var skip = Math.Max(0, query.Skip);
        var excerpt = Math.Clamp(settings.ExcerptLength, 40, 2000);

        var documents = context.SearchDocuments.AsNoTracking();

        if (query.Kind is { } kind)
        {
            documents = documents.Where(document => document.EntityType == (SearchEntityKind)kind);
        }

        if (PageFilter(query, status) is { } pages)
        {
            // Restricted to pages by construction rather than by a separate clause: templates,
            // owners, tags and review dates are page facts, so asking for one of them and a media
            // item at the same time is a query with no answer, and it says so by returning nothing.
            var ids = pages.Select(page => page.Id);

            documents = documents.Where(document =>
                document.EntityType == SearchEntityKind.Page && ids.Contains(document.EntityId));
        }

        if (query.ModifiedFrom is { } from)
        {
            documents = documents.Where(document => document.UpdatedOn >= from);
        }

        if (query.ModifiedTo is { } to)
        {
            documents = documents.Where(document => document.UpdatedOn <= to);
        }

        var fullText = await capabilities.FullTextAsync(context, cancellationToken);
        var condition = fullText ? FullTextQuery.Build(query.Text) : null;
        var pattern = condition is null && !string.IsNullOrWhiteSpace(query.Text)
            ? $"%{Escape(query.Text.Trim())}%"
            : null;

        if (condition is not null)
        {
            documents = documents.Where(document =>
                EF.Functions.Contains(document.Title, condition) ||
                // The bang is the nullable annotation rather than a claim about the data: CONTAINS
                // over a NULL column matches nothing, which is exactly what it should do.
                EF.Functions.Contains(document.Body!, condition) ||
                EF.Functions.Contains(document.Keywords!, condition));
        }
        else if (pattern is not null)
        {
            documents = documents.Where(document =>
                EF.Functions.Like(document.Title, pattern, LikeEscape) ||
                EF.Functions.Like(document.Body, pattern, LikeEscape) ||
                EF.Functions.Like(document.Keywords, pattern, LikeEscape));
        }

        var total = await documents.CountAsync(cancellationToken);

        // Title before body, then most recently touched. Not a relevance score: ranking a backoffice
        // search by term frequency puts the long page above the one actually called "Pricing", and
        // an editor searching their own site is nearly always looking for a title they half remember.
        var ordered = condition is not null
            ? documents
                .OrderBy(document => EF.Functions.Contains(document.Title, condition) ? 0 : 1)
                .ThenByDescending(document => document.UpdatedOn)
            : pattern is not null
                ? documents
                    .OrderBy(document => EF.Functions.Like(document.Title, pattern, LikeEscape) ? 0 : 1)
                    .ThenByDescending(document => document.UpdatedOn)
                : documents.OrderByDescending(document => document.UpdatedOn);

        var found = await ordered
            .Skip(skip)
            .Take(limit)
            .Select(document => new SearchHit(
                (SearchResultKind)document.EntityType,
                document.EntityId,
                document.Title,
                document.Url,
                // Cut in SQL rather than after: a body column holds the whole page, and pulling
                // twenty-five of them across to show two lines each is the sort of query that only
                // hurts on the site large enough to need search.
                document.Body == null
                    ? null
                    : document.Body.Length <= excerpt ? document.Body : document.Body.Substring(0, excerpt),
                document.IsPublished,
                document.UpdatedOn))
            .ToListAsync(cancellationToken);

        var visible = await FilterAsync(found, cancellationToken);

        return CmsResult<SearchResults>.Success(new SearchResults(visible, total, fullText));
    }

    /// <summary>The page query behind the page-only filters, or null when none were asked for.</summary>
    private IQueryable<Page>? PageFilter(SearchQuery query, PageVersionStatus? status)
    {
        var asked =
            query.TemplateId is not null ||
            status is not null ||
            query.OwnerUserId is not null ||
            !string.IsNullOrWhiteSpace(query.Tag) ||
            query.HasUnpublishedChanges is not null ||
            query.PastReviewDate;

        if (!asked) return null;

        var pages = context.Pages.AsNoTracking();

        if (query.TemplateId is { } templateId)
        {
            pages = pages.Where(page => page.TemplateId == templateId);
        }

        if (status is { } wanted)
        {
            pages = pages.Where(page => page.DraftVersion!.Status == wanted);
        }

        if (query.OwnerUserId is { } ownerId)
        {
            pages = pages.Where(page => page.OwnerUserId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(query.Tag))
        {
            // By slug or by name, because the filter is reached both from a tag chip (which carries
            // the slug) and from a box somebody typed a label into.
            var tag = query.Tag.Trim();

            pages = pages.Where(page => context.PageTags.Any(applied =>
                applied.PageId == page.Id &&
                (applied.Tag.Slug == tag || applied.Tag.Name == tag)));
        }

        if (query.HasUnpublishedChanges is { } unpublished)
        {
            // "Different from what is published" includes "never published", which is the state an
            // editor means when they ask what still needs publishing.
            pages = unpublished
                ? pages.Where(page =>
                    page.PublishedVersionId == null || page.PublishedVersionId != page.DraftVersionId)
                : pages.Where(page =>
                    page.PublishedVersionId != null && page.PublishedVersionId == page.DraftVersionId);
        }

        if (query.PastReviewDate)
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

            pages = pages.Where(page => page.ReviewByDate != null && page.ReviewByDate < today);
        }

        return pages;
    }

    /// <summary>Drops page hits the caller's access rules hide.</summary>
    /// <remarks>
    /// Media and reusable items are not in the tree, so no rule can bear on them; only page hits
    /// need the paths this fetches, and only when a rule exists at all.
    /// </remarks>
    private async Task<IReadOnlyList<SearchHit>> FilterAsync(
        List<SearchHit> hits,
        CancellationToken cancellationToken)
    {
        var readable = await acl.GetFilterAsync(CmsPermissions.ContentRead, cancellationToken);

        if (readable.IsUnrestricted) return hits;

        var pageIds = hits
            .Where(hit => hit.Kind is SearchResultKind.Page)
            .Select(hit => hit.Id)
            .ToArray();

        if (pageIds.Length == 0) return hits;

        var paths = await context.Pages
            .AsNoTracking()
            .Where(page => pageIds.Contains(page.Id))
            .ToDictionaryAsync(page => page.Id, page => page.Path, cancellationToken);

        return
        [
            .. hits.Where(hit =>
                hit.Kind is not SearchResultKind.Page ||
                (paths.TryGetValue(hit.Id, out var path) && readable.Allows(hit.Id, path))),
        ];
    }

    /// <summary>Makes a term's own wildcards literal.</summary>
    private static string Escape(string term) => term
        .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
        .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
        .Replace("_", LikeEscape + "_", StringComparison.Ordinal)
        .Replace("[", LikeEscape + "[", StringComparison.Ordinal);
}
