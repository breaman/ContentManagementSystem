using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Media.Delivery;

/// <inheritdoc cref="IMediaResolver" />
/// <param name="contexts">Makes a context of its own for each resolve (ADR-0022).</param>
/// <remarks>
/// A context per resolve rather than the request's, because this runs from a field renderer's
/// <c>OnParametersSetAsync</c> and Blazor overlaps sibling renderers' asynchronous lifecycle
/// methods. The read is <c>AsNoTracking</c> and writes nothing, so there is no unit of work that
/// sharing one context would preserve.
/// </remarks>
public sealed class MediaResolver(IDbContextFactory<ApplicationDbContext> contexts) : IMediaResolver
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, ResolvedMedia>> ResolveAsync(
        IEnumerable<int> mediaIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaIds);

        var ids = mediaIds.Distinct().ToList();

        if (ids.Count == 0) return new Dictionary<int, ResolvedMedia>();

        await using var context = await contexts.CreateDbContextAsync(cancellationToken);

        // Only the columns a render reads. The storage key is deliberately not among them: a
        // renderer addresses an item through signed URLs, and a key in reach is a key something
        // eventually puts in an href (spec section 13.5).
        var rows = await context.MediaItems
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.MediaKind,
                item.ContentType,
                item.OriginalFileName,
                item.Width,
                item.Height,
                item.AltText,
                item.IsDecorative,
                item.Title,
                item.Caption,
                item.Credit,
                item.FocalPointX,
                item.FocalPointY,
                item.EditsJson,
                item.EditsVersion,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var resolved = new Dictionary<int, ResolvedMedia>(rows.Count);

        foreach (var row in rows)
        {
            var edits = MediaEdits.Parse(row.EditsJson);

            // The focal point has two homes, and the columns are the older of them: an item edited
            // through the image editor carries one inside the edit document, and one that has only
            // ever had a focal point set carries it in the columns. The document wins where both
            // exist, because it is what the processor reads when it renders.
            if (edits.FocalPoint is null && row.FocalPointX is { } x && row.FocalPointY is { } y)
            {
                edits = edits with { FocalPoint = new NormalizedPoint(x, y) };
            }

            resolved[row.Id] = new ResolvedMedia(
                row.Id,
                row.MediaKind,
                row.ContentType,
                row.OriginalFileName,
                row.Width,
                row.Height,
                row.AltText,
                row.IsDecorative,
                row.Title,
                row.Caption,
                row.Credit,
                edits,
                row.EditsVersion);
        }

        return resolved;
    }
}
