using System.Text;
using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Content;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Search;

/// <inheritdoc cref="ISearchIndexer" />
/// <param name="context">The application database context.</param>
/// <param name="fieldTypes">The field types this deployment knows about.</param>
/// <param name="clock">Source of the <c>UpdatedOn</c> stamp the reconcile compares against.</param>
/// <param name="logger">Log for a zone whose text could not be extracted.</param>
/// <remarks>
/// <strong>The index describes working content, not published content.</strong> A page's document is
/// built from its draft, which is what the backoffice search box is for — an editor looking for the
/// paragraph they wrote this morning would not find it in an index of what is live. Whether the
/// thing is published is carried as a column instead, so the future public search (spec section
/// 17.2) is a filter rather than a second index.
/// <para>
/// Text extraction is driven by the payload's own <c>type</c> discriminators rather than by the
/// template schema, following <c>ReferenceIndexer</c>: a zone removed from its template still holds
/// content (spec section 8.5), and a schema-driven walk would quietly stop indexing it.
/// </para>
/// </remarks>
public sealed class SearchIndexer(
    ApplicationDbContext context,
    IFieldTypeRegistry fieldTypes,
    TimeProvider clock,
    ILogger<SearchIndexer> logger) : ISearchIndexer
{
    /// <summary>How many things one reconcile batch rebuilds at a time.</summary>
    private const int ReconcileBatchSize = 200;

    /// <inheritdoc />
    public async Task<int> IndexAsync(
        SearchEntityKind kind,
        IReadOnlyList<int> entityIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityIds);

        var ids = entityIds.Where(id => id > 0).Distinct().ToArray();

        if (ids.Length == 0) return 0;

        var documents = await context.SearchDocuments
            .Where(document => document.EntityType == kind && ids.Contains(document.EntityId))
            .ToDictionaryAsync(document => document.EntityId, cancellationToken);

        var projections = kind switch
        {
            SearchEntityKind.Page => await ProjectPagesAsync(ids, cancellationToken),
            SearchEntityKind.Media => await ProjectMediaAsync(ids, cancellationToken),
            SearchEntityKind.Reusable => await ProjectReusableAsync(ids, cancellationToken),
            _ => [],
        };

        var now = clock.GetUtcNow();
        var changed = 0;

        foreach (var projection in projections)
        {
            if (!documents.Remove(projection.EntityId, out var document))
            {
                document = new SearchDocument { EntityType = kind, EntityId = projection.EntityId };

                context.SearchDocuments.Add(document);
            }

            document.Title = projection.Title;
            document.Body = projection.Body;
            document.Keywords = projection.Keywords;
            document.Url = projection.Url;
            document.IsPublished = projection.IsPublished;
            document.UpdatedOn = now;

            changed++;
        }

        // Whatever is left described something the query above did not find: recycled, hard-deleted,
        // or an id that was never real. All three mean the same thing to a search result.
        foreach (var orphan in documents.Values)
        {
            context.SearchDocuments.Remove(orphan);

            changed++;
        }

        await context.SaveChangesAsync(cancellationToken);

        return changed;
    }

    /// <inheritdoc />
    public async Task<SearchReconcileReport> ReconcileAsync(CancellationToken cancellationToken = default)
    {
        var rebuilt = 0;
        var removed = 0;
        var examined = 0;

        foreach (var kind in Enum.GetValues<SearchEntityKind>())
        {
            var live = await LiveAsync(kind, cancellationToken);

            examined += live.Count;

            var indexed = await context.SearchDocuments
                .AsNoTracking()
                .Where(document => document.EntityType == kind)
                .Select(document => new { document.EntityId, document.UpdatedOn })
                .ToDictionaryAsync(document => document.EntityId, document => document.UpdatedOn, cancellationToken);

            // Stale as well as missing. A document older than the thing it describes is the shape a
            // dropped outbox message leaves behind, and it is invisible to a check that only asks
            // whether a row exists.
            var stale = live
                .Where(entry => !indexed.TryGetValue(entry.Key, out var updatedOn) || updatedOn < entry.Value)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (var batch in stale.Chunk(ReconcileBatchSize))
            {
                rebuilt += await IndexAsync(kind, batch, cancellationToken);
            }

            var orphans = indexed.Keys.Where(id => !live.ContainsKey(id)).ToArray();

            foreach (var batch in orphans.Chunk(ReconcileBatchSize))
            {
                removed += await context.SearchDocuments
                    .Where(document => document.EntityType == kind && batch.Contains(document.EntityId))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }

        if (rebuilt > 0 || removed > 0)
        {
            logger.LogWarning(
                "The search reconcile rebuilt {RebuiltCount} document(s) and removed {RemovedCount}, " +
                "over {ExaminedCount} item(s). Anything above zero means indexing fell behind.",
                rebuilt,
                removed,
                examined);
        }

        return new SearchReconcileReport(rebuilt, removed, examined);
    }

    /// <summary>Every live thing of one kind, with the instant it last changed.</summary>
    /// <remarks>
    /// A page's own row does not change when somebody types into its draft, so the later of the two
    /// stamps is what the index has to keep up with. Taking the page's alone would make the
    /// reconcile agree with an index that is missing every edit since the page was created.
    /// </remarks>
    private async Task<Dictionary<int, DateTimeOffset>> LiveAsync(
        SearchEntityKind kind,
        CancellationToken cancellationToken) =>
        kind switch
        {
            SearchEntityKind.Page => await context.Pages
                .AsNoTracking()
                .Select(page => new
                {
                    page.Id,
                    ChangedOn = page.DraftVersion != null && page.DraftVersion.ModifiedOn > page.ModifiedOn
                        ? page.DraftVersion.ModifiedOn
                        : page.ModifiedOn,
                })
                .ToDictionaryAsync(row => row.Id, row => row.ChangedOn ?? DateTimeOffset.MinValue, cancellationToken),

            SearchEntityKind.Media => await context.MediaItems
                .AsNoTracking()
                .Select(item => new { item.Id, ChangedOn = item.ModifiedOn })
                .ToDictionaryAsync(row => row.Id, row => row.ChangedOn ?? DateTimeOffset.MinValue, cancellationToken),

            SearchEntityKind.Reusable => await context.ReusableContents
                .AsNoTracking()
                .Select(item => new
                {
                    item.Id,
                    ChangedOn = item.DraftVersion != null && item.DraftVersion.ModifiedOn > item.ModifiedOn
                        ? item.DraftVersion.ModifiedOn
                        : item.ModifiedOn,
                })
                .ToDictionaryAsync(row => row.Id, row => row.ChangedOn ?? DateTimeOffset.MinValue, cancellationToken),

            _ => [],
        };

    private async Task<IReadOnlyList<SearchProjection>> ProjectPagesAsync(
        int[] ids,
        CancellationToken cancellationToken)
    {
        // The global query filter already hides recycled pages, which is what makes a recycled page
        // fall out of the index: it is simply not found, and its document is removed with the rest
        // of the orphans.
        var rows = await context.Pages
            .AsNoTracking()
            .Where(page => ids.Contains(page.Id))
            .Select(page => new
            {
                page.Id,
                page.Slug,
                IsPublished = page.PublishedVersionId != null,
                Title = page.DraftVersion != null ? page.DraftVersion.Title : page.PublishedVersion!.Title,
                ContentJson = page.DraftVersion != null
                    ? page.DraftVersion.ContentJson
                    : page.PublishedVersion!.ContentJson,
                Url = context.PageRoutes
                    .Where(route => route.PageId == page.Id && route.IsPrimary)
                    .OrderByDescending(route => route.IsPublished)
                    .Select(route => route.Url)
                    .FirstOrDefault(),
                Tags = context.PageTags
                    .Where(tag => tag.PageId == page.Id)
                    .Select(tag => tag.Tag.Name)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new SearchProjection(
                row.Id,
                Title(row.Title),
                ExtractBody(row.ContentJson, SearchEntityKind.Page, row.Id),
                Keywords(row.Slug, string.Join(' ', row.Tags)),
                row.Url,
                row.IsPublished)),
        ];
    }

    private async Task<IReadOnlyList<SearchProjection>> ProjectMediaAsync(
        int[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.MediaItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.OriginalFileName,
                item.FileName,
                item.AltText,
                item.Caption,
                item.Credit,
            })
            .ToListAsync(cancellationToken);

        return
        [
            // Media is indexed as soon as it is uploaded and has no publish step of its own, so its
            // documents are published: an image in the library is available to every page that
            // references it, and a future public search filtering on this column wants the page's
            // state rather than the image's.
            .. rows.Select(row => new SearchProjection(
                row.Id,
                Title(string.IsNullOrWhiteSpace(row.Title) ? row.OriginalFileName : row.Title),
                Keywords(row.Caption, row.Credit),
                Keywords(row.FileName, row.OriginalFileName, row.AltText),
                Url: null,
                IsPublished: true)),
        ];
    }

    private async Task<IReadOnlyList<SearchProjection>> ProjectReusableAsync(
        int[] ids,
        CancellationToken cancellationToken)
    {
        var rows = await context.ReusableContents
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.Key,
                item.Name,
                item.Description,
                IsPublished = item.PublishedVersionId != null,
                ContentJson = item.DraftVersion != null
                    ? item.DraftVersion.ContentJson
                    : item.PublishedVersion!.ContentJson,
            })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => new SearchProjection(
                row.Id,
                Title(row.Name),
                ExtractBody(row.ContentJson, SearchEntityKind.Reusable, row.Id),
                Keywords(row.Key, row.Description),
                Url: null,
                row.IsPublished)),
        ];
    }

    /// <summary>Reduces a payload to the words in it.</summary>
    /// <remarks>
    /// One zone's failure costs that zone rather than the document. A field type that throws on a
    /// value it wrote is a bug, but the reaction to it must not be a page that stops being findable
    /// at all — and the log line is what makes the bug visible.
    /// </remarks>
    private string? ExtractBody(string? contentJson, SearchEntityKind kind, int entityId)
    {
        if (!ContentPayload.TryParse(contentJson, out var payload) || !payload.HasZones) return null;

        var builder = new StringBuilder();

        foreach (var zone in payload.Zones.EnumerateObject())
        {
            if (ReadTypeKey(zone.Value) is not { } typeKey) continue;

            // A key nothing is registered under contributes nothing, the way it does everywhere else
            // a payload is walked: content outlives the code deployed when it was written.
            if (fieldTypes.Find(typeKey) is not { } fieldType) continue;

            if (!fieldType.Capabilities.HasFlag(Shared.Contracts.Fields.FieldTypeCapabilities.Searchable)) continue;

            try
            {
                var text = fieldType.ExtractSearchText(zone.Value);

                if (string.IsNullOrWhiteSpace(text)) continue;

                if (builder.Length > 0) builder.Append(' ');

                builder.Append(text);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Extracting search text from zone '{ZoneKey}' ('{FieldTypeKey}') of {EntityKind} " +
                    "{EntityId} failed; the zone contributes nothing and the rest is still indexed.",
                    zone.Name,
                    typeKey,
                    kind,
                    entityId);
            }
        }

        return builder.Length == 0 ? null : SearchText.Collapse(builder.ToString());
    }

    private static string? ReadTypeKey(JsonElement value) =>
        value.ValueKind is JsonValueKind.Object &&
        value.TryGetProperty(ContentPayloadMembers.Type, out var type) &&
        type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;

    /// <summary>Keeps a title inside the column it goes in.</summary>
    private static string Title(string? title) =>
        string.IsNullOrWhiteSpace(title)
            ? "(untitled)"
            : title.Length <= FieldLengths.ContentTitle ? title : title[..FieldLengths.ContentTitle];

    /// <summary>Joins the odds and ends that go in one keyword column.</summary>
    private static string? Keywords(params string?[] parts)
    {
        var text = string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));

        return string.IsNullOrWhiteSpace(text) ? null : SearchText.Collapse(text);
    }

    /// <summary>One thing, reduced to what the index stores about it.</summary>
    private sealed record SearchProjection(
        int EntityId,
        string Title,
        string? Body,
        string? Keywords,
        string? Url,
        bool IsPublished);
}
