using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Preview;

/// <inheritdoc cref="IPreviewContentService" />
/// <param name="context">The application database context.</param>
/// <param name="schemas">Resolves the captured revision the payload names.</param>
/// <param name="logger">Log for content that loads but cannot be read.</param>
/// <remarks>
/// One query, no tracking, no writes — the same shape as <c>PublishedContentService</c>, because
/// preview fidelity is only structural if the two paths differ in what they select and in nothing
/// else. Everything after the load is literally the same code: the same payload parse, the same
/// captured-schema resolution, the same <c>PublishedContent</c>, the same components.
/// <para>
/// The soft-delete query filter is left in place. A recycled page is not previewable content, and
/// <c>IgnoreQueryFilters</c> here would make preview a way to keep reading a page nobody can see in
/// the tree. The token path distinguishes the two outcomes for the reviewer's benefit — see
/// <c>PreviewTokenService</c> — but it does so by asking about the token, not by widening this.
/// </para>
/// </remarks>
public sealed class PreviewContentService(
    ApplicationDbContext context,
    IContentSchemaCatalog schemas,
    ILogger<PreviewContentService> logger) : IPreviewContentService
{
    /// <inheritdoc />
    public async Task<PublishedContent?> GetAsync(
        int pageId,
        int? versionId = null,
        CancellationToken cancellationToken = default)
    {
        if (pageId <= 0) return null;

        // The version is selected by a join on the page rather than fetched separately, so a
        // version id belonging to a different page can never be served under this page's URL and
        // metadata — the pair is the address, and splitting the query would make the mismatch a
        // check somebody could forget.
        var row = await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .SelectMany(
                page => context.PageVersions.Where(version =>
                    version.PageId == page.Id &&
                    (versionId != null
                        ? version.Id == versionId
                        : version.Id == (page.DraftVersionId ?? page.PublishedVersionId))),
                (page, version) => new PreviewRow(
                    page.Id,
                    page.PublicId,
                    version.Id,
                    version.VersionNumber,
                    version.Title,
                    page.TemplateId,
                    page.Template.Key,
                    version.TemplateRevision,
                    page.PublishedVersionId == version.Id,
                    version.PublishedOn,
                    version.ModifiedOn,
                    new PublishedSeo(
                        version.MetaTitle,
                        version.MetaDescription,
                        version.CanonicalUrl,
                        version.RobotsIndex,
                        version.RobotsFollow,
                        version.OgTitle,
                        version.OgDescription,
                        version.OgImageMediaId,
                        version.OgType,
                        version.TwitterCard,
                        version.StructuredDataJson),
                    version.ContentJson,

                    // The draft route first. It exists from the moment a page is created (task
                    // P3-04) and it tracks the page's current position in the tree, which is what an
                    // editor previewing a move needs to see; the published route is the fallback for
                    // the rare page that has lost its draft route.
                    context.PageRoutes
                        .Where(route => route.PageId == page.Id && !route.IsPublished)
                        .OrderByDescending(route => route.IsPrimary)
                        .Select(route => route.Url)
                        .FirstOrDefault(),
                    context.PageRoutes
                        .Where(route => route.PageId == page.Id && route.IsPublished)
                        .OrderByDescending(route => route.IsPrimary)
                        .Select(route => route.Url)
                        .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null) return null;

        ContentPayload payload;

        try
        {
            payload = ContentPayload.Parse(row.ContentJson);
        }
        catch (JsonException exception)
        {
            // Stored content that is not well-formed JSON at all. Worth logging as a fault here even
            // more than on the public path: preview is where an editor would be looking when they
            // discover it, and "the preview is blank" with nothing in the log is unanswerable.
            logger.LogError(
                exception,
                "Page {PageId} version {VersionId} has unreadable content and cannot be previewed.",
                row.PageId,
                row.VersionId);

            return null;
        }

        // The revision the payload captured, not the template's current one (spec section 8.5).
        var schema = schemas.TryGetTemplate(row.TemplateKey, row.TemplateRevision, out var resolved)
            ? resolved
            : null;

        return new PublishedContent(
            row.PageId,
            row.PublicId,
            row.VersionId,
            row.VersionNumber,
            row.Title,
            row.DraftUrl ?? row.PublishedUrl ?? SiteUrls.Root,
            row.TemplateId,
            row.TemplateKey,
            row.TemplateRevision,
            row.IsPublished,
            row.PublishedOn,
            row.ModifiedOn,
            row.Seo,
            payload,
            schema);
    }

    /// <inheritdoc />
    public async Task<PreviewVersionInfo?> DescribeAsync(
        int pageId,
        int? versionId = null,
        CancellationToken cancellationToken = default)
    {
        if (pageId <= 0) return null;

        // The same join-through-the-page shape as GetAsync, for the same reason: a version id
        // belonging to another page must not be describable under this page's chrome either.
        return await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId)
            .SelectMany(
                page => context.PageVersions.Where(version =>
                    version.PageId == page.Id &&
                    (versionId != null
                        ? version.Id == versionId
                        : version.Id == (page.DraftVersionId ?? page.PublishedVersionId))),
                (page, version) => new PreviewVersionInfo(
                    page.Id,
                    version.Id,
                    version.Title,
                    version.VersionNumber,
                    version.Status.ToString(),
                    page.PublishedVersionId == version.Id,
                    page.DraftVersionId == version.Id))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>The shape the query projects into, before the payload is parsed.</summary>
    /// <remarks>
    /// Two URL columns rather than one, unlike the published row: preview prefers the draft route
    /// and the choice between them is made in memory, because a coalesce inside the projection would
    /// be a third correlated subquery for no benefit.
    /// </remarks>
    private sealed record PreviewRow(
        int PageId,
        Guid PublicId,
        int VersionId,
        int VersionNumber,
        string Title,
        int TemplateId,
        string TemplateKey,
        int TemplateRevision,
        bool IsPublished,
        DateTimeOffset? PublishedOn,
        DateTimeOffset? ModifiedOn,
        PublishedSeo Seo,
        string ContentJson,
        string? DraftUrl,
        string? PublishedUrl);
}
