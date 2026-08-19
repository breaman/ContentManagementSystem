using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Delivery;

/// <inheritdoc cref="IPublishedContentService" />
/// <param name="context">The application database context.</param>
/// <param name="schemas">Resolves the captured revision the payload names.</param>
/// <param name="logger">Log for content that loads but cannot be read.</param>
/// <remarks>
/// One query, no tracking, and no writes. The change tracker is off because nothing here is going to
/// be saved and identity resolution over a page's whole version history is pure cost on the hottest
/// path the application has.
/// <para>
/// The projection selects through <c>page.PublishedVersion</c> and never mentions
/// <c>DraftVersion</c>. That is what makes acceptance criterion <c>P3 #3</c> — further draft edits
/// do not change the anonymous response — a property of the SQL rather than of a reviewer noticing:
/// the draft row is not in the result set to be picked by mistake.
/// </para>
/// </remarks>
public sealed class PublishedContentService(
    ApplicationDbContext context,
    IContentSchemaCatalog schemas,
    ILogger<PublishedContentService> logger) : IPublishedContentService
{
    /// <inheritdoc />
    public async Task<PublishedContent?> GetAsync(
        int pageId,
        CancellationToken cancellationToken = default) =>
        await GetRowAsync(pageId, cancellationToken) is { } row ? Materialize(row) : null;

    /// <summary>
    /// Reads the stored row for a page's published version, without parsing anything.
    /// </summary>
    /// <param name="pageId">The page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The row, or null when the page has no published version.</returns>
    /// <remarks>
    /// Split out from <see cref="GetAsync"/> so the cache in front of this can store something a
    /// serializer can write: the row is scalars and one JSON string, while a
    /// <see cref="PublishedContent"/> carries a live <c>JsonElement</c> and a captured schema, and
    /// <c>HybridCache</c> serializes everything it stores whatever the type claims about its own
    /// immutability (task P8-08).
    /// </remarks>
    public async Task<PublishedContentRow?> GetRowAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (pageId <= 0) return null;

        // The soft-delete query filter is left in place on purpose: a recycled page is not published
        // content, and IgnoreQueryFilters here would make the recycle bin a way to keep serving a
        // page nobody can see in the tree.
        var row = await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == pageId && page.PublishedVersionId != null)
            .Select(page => new PublishedContentRow(
                page.Id,
                page.PublicId,
                page.PublishedVersion!.Id,
                page.PublishedVersion.VersionNumber,
                page.PublishedVersion.Title,
                page.TemplateId,
                page.Template.Key,
                page.PublishedVersion.TemplateRevision,
                page.PublishedVersion.PublishedOn,
                page.PublishedVersion.ModifiedOn,
                new PublishedSeo(
                    page.PublishedVersion.MetaTitle,
                    page.PublishedVersion.MetaDescription,
                    page.PublishedVersion.CanonicalUrl,
                    page.PublishedVersion.RobotsIndex,
                    page.PublishedVersion.RobotsFollow,
                    page.PublishedVersion.OgTitle,
                    page.PublishedVersion.OgDescription,
                    page.PublishedVersion.OgImageMediaId,
                    page.PublishedVersion.OgType,
                    page.PublishedVersion.TwitterCard,
                    page.PublishedVersion.StructuredDataJson),
                page.PublishedVersion.ContentJson,
                context.PageRoutes
                    .Where(route => route.PageId == page.Id && route.IsPublished)
                    .OrderByDescending(route => route.IsPrimary)
                    .Select(route => route.Url)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

        return row;
    }

    /// <summary>
    /// Turns a stored row into the delivery result, parsing the payload and resolving its schema.
    /// </summary>
    /// <param name="row">The row, from this service or from the cache in front of it.</param>
    /// <returns>The content, or null when the stored payload cannot be read at all.</returns>
    public PublishedContent? Materialize(PublishedContentRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        ContentPayload payload;

        try
        {
            payload = ContentPayload.Parse(row.ContentJson);
        }
        catch (JsonException exception)
        {
            // Stored content that is not well-formed JSON at all. Nothing downstream can do anything
            // with it, and rendering half a document is worse than the 404 the null produces — but it
            // is a fault rather than a missing page, so it is logged as one.
            logger.LogError(
                exception,
                "Page {PageId} version {VersionId} has unreadable content and cannot be delivered.",
                row.PageId,
                row.VersionId);

            return null;
        }

        // The revision the payload captured, not the template's current one (spec section 8.5).
        // Failure is ordinary — a deleted template, an environment promoted out of order — and the
        // catalog has already logged it. The page still renders: every value names the field type
        // that wrote it, and what is lost is the configuration, not the content.
        var schema = schemas.TryGetTemplate(row.TemplateKey, row.TemplateRevision, out var resolved)
            ? resolved
            : null;

        return new PublishedContent(
            row.PageId,
            row.PublicId,
            row.VersionId,
            row.VersionNumber,
            row.Title,
            row.Url ?? SiteUrls.Root,
            row.TemplateId,
            row.TemplateKey,
            row.TemplateRevision,
            IsPublished: true,
            row.PublishedOn,
            row.ModifiedOn,
            row.Seo,
            payload,
            schema);
    }

    /// <summary>
    /// The shape the query projects into, before the payload is parsed.
    /// </summary>
    /// <remarks>
    /// A named record rather than an anonymous type only so the projection reads as a list of
    /// columns; EF translates either identically. It stops one column short of
    /// <see cref="PublishedContent"/> — the content arrives as text and becomes a payload afterwards,
    /// because parsing is not something a query provider can be asked to do.
    /// <para>
    /// Public, and the reason is the cache: this is the form that can be serialized and stored, and
    /// <see cref="Materialize"/> is what turns it back into something renderable (task P8-08).
    /// </para>
    /// </remarks>
    public sealed record PublishedContentRow(
        int PageId,
        Guid PublicId,
        int VersionId,
        int VersionNumber,
        string Title,
        int TemplateId,
        string TemplateKey,
        int TemplateRevision,
        DateTimeOffset? PublishedOn,
        DateTimeOffset? ModifiedOn,
        PublishedSeo Seo,
        string ContentJson,
        string? Url);
}
