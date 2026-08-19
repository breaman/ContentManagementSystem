using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Library;

/// <inheritdoc cref="IMediaLibraryService" />
/// <param name="context">The application database context.</param>
/// <param name="store">Where originals and renditions live, for the one operation that removes them.</param>
/// <param name="impact">Answers where an item is used, which is the permanent-delete guard.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Identity of the caller, stamped on a soft delete.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for every edit, delete, and refusal.</param>
public sealed class MediaLibraryService(
    ApplicationDbContext context,
    IMediaStore store,
    IReferenceQueryService impact,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ICacheInvalidationQueue cacheInvalidation,
    ISearchIndexQueue search,
    ILogger<MediaLibraryService> logger) : IMediaLibraryService
{
    /// <inheritdoc />
    public async Task<CmsResult<MediaListResult>> ListAsync(
        MediaQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<MediaListResult>("Browsing the media library is not permitted.");
        }

        // IgnoreQueryFilters and then filtering by hand, because the bin is a view of the library
        // rather than a separate place: one query shape serves both, and the recycle-bin screen
        // cannot drift from the browser it is a mode of.
        var items = context.MediaItems.AsNoTracking().IgnoreQueryFilters()
            .Where(item => item.IsDeleted == query.DeletedOnly);

        if (query.FolderId is { } folderId)
        {
            if (query.IncludeDescendants)
            {
                var folder = await context.MediaFolders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(candidate => candidate.Id == folderId, cancellationToken);

                if (folder is null) return NotFound<MediaListResult>($"Folder {folderId} does not exist.");

                // The prefix match the materialized path exists for. Everything filed anywhere under
                // the folder, in one indexed scan rather than a walk down the tree.
                var descendants = context.MediaFolders
                    .AsNoTracking()
                    .Where(candidate => candidate.Path.StartsWith(folder.Path))
                    .Select(candidate => (int?)candidate.Id);

                items = items.Where(item => descendants.Contains(item.FolderId));
            }
            else
            {
                items = items.Where(item => item.FolderId == folderId);
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Kind))
        {
            // An unrecognised kind is refused rather than quietly matching nothing: "why is my
            // library empty" is a much worse question to be left with than "Vidoe is not a kind".
            if (!Enum.TryParse<MediaKind>(query.Kind, ignoreCase: true, out var kind))
            {
                return CmsResult<MediaListResult>.Invalid(
                    MediaCodes.NotFound,
                    $"'{query.Kind}' is not a kind of media. Use one of: " +
                    $"{string.Join(", ", Enum.GetNames<MediaKind>())}.",
                    nameof(MediaQuery.Kind));
            }

            items = items.Where(item => item.MediaKind == kind);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();

            items = items.Where(item =>
                EF.Functions.Like(item.OriginalFileName, $"%{term}%") ||
                (item.Title != null && EF.Functions.Like(item.Title, $"%{term}%")) ||
                (item.AltText != null && EF.Functions.Like(item.AltText, $"%{term}%")));
        }

        if (query.UnusedOnly)
        {
            // "Nothing points here" over the projected reference rows, which is the same authority
            // the permanent-delete guard consults — so an item this filter offers up is one the
            // delete button will actually accept.
            var referenced = context.ContentReferences
                .AsNoTracking()
                .Where(row => row.TargetType == ContentReferenceTargetType.Media)
                .Select(row => row.TargetId);

            items = items.Where(item => !referenced.Contains(item.Id));
        }

        var total = await items.CountAsync(cancellationToken);

        var take = Math.Clamp(query.Take, 1, MediaQuery.MaxTake);
        var skip = Math.Max(query.Skip, 0);

        var page = await items
            .OrderByDescending(item => item.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return CmsResult<MediaListResult>.Success(
            new MediaListResult([.. page.Select(MediaProjections.ToDetail)], total, skip, take));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDetail>> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return Forbidden<MediaDetail>("Reading the media library is not permitted.");
        }

        // Deleted items are readable by id. The bin screen needs their metadata to offer a restore,
        // and the where-used panel of a page that still points at one needs a name to show.
        var item = await context.MediaItems
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        return item is null
            ? NotFound<MediaDetail>(id)
            : CmsResult<MediaDetail>.Success(MediaProjections.ToDetail(item));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaDetail>("Editing media metadata is not permitted.");
        }

        var item = await context.MediaItems.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<MediaDetail>(id);

        var altText = Trim(request.AltText.Or(item.AltText));
        var isDecorative = request.IsDecorative.Or(item.IsDecorative);

        // The alt-text rule of spec section 13.7 applies to every write, not only to the upload.
        // Without this, an editor could satisfy the upload check and then clear the field, and the
        // publish-time validation would be the first thing to notice — on somebody else's page.
        if (item.MediaKind is MediaKind.Image && !isDecorative && string.IsNullOrWhiteSpace(altText))
        {
            return CmsResult<MediaDetail>.Invalid(
                MediaCodes.AltTextRequired,
                "Describe the image for people who cannot see it, or mark it decorative.",
                nameof(PatchMediaRequest.AltText));
        }

        if (request.FolderId.IsSet && request.FolderId.Value is { } folderId &&
            !await context.MediaFolders.AnyAsync(folder => folder.Id == folderId, cancellationToken))
        {
            return NotFound<MediaDetail>($"Folder {folderId} does not exist.");
        }

        var title = Trim(request.Title.Or(item.Title));
        var caption = Trim(request.Caption.Or(item.Caption));
        var credit = Trim(request.Credit.Or(item.Credit));

        if (TooLong(altText, title, caption, credit) is { } tooLong) return tooLong;

        // Decorative wins over any text supplied alongside it. Storing both would leave two answers
        // to "what does a screen reader say", and the renderer would have to pick one.
        item.AltText = isDecorative ? null : altText;
        item.IsDecorative = isDecorative;
        item.Title = title;
        item.Caption = caption;
        item.Credit = credit;
        item.FolderId = request.FolderId.Or(item.FolderId);

        if (RowVersions.TryApply(context.Entry(item), request.ExpectedRowVersion) is false)
        {
            return CmsResult<MediaDetail>.Invalid(
                MediaCodes.Conflict,
                "The version token sent with this change is not one this server issued.",
                nameof(PatchMediaRequest.ExpectedRowVersion));
        }

        return await SaveAsync(item, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaDetail>("Editing media is not permitted.");
        }

        if (request.Edits is null || !request.Edits.IsValid)
        {
            return CmsResult<MediaDetail>.Invalid(
                MediaCodes.EditsInvalid,
                "Rotation must be 0, 90, 180, or 270, and any crop or focal point must lie inside " +
                "the image.",
                nameof(SetMediaEditsRequest.Edits));
        }

        var item = await context.MediaItems.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<MediaDetail>(id);

        if (item.MediaKind is not MediaKind.Image)
        {
            return CmsResult<MediaDetail>.Invalid(
                MediaCodes.NotAnImage,
                "Only images can be rotated, cropped, or given a focal point.");
        }

        ApplyEdits(item, request.Edits);

        if (RowVersions.TryApply(context.Entry(item), request.ExpectedRowVersion) is false)
        {
            return CmsResult<MediaDetail>.Invalid(
                MediaCodes.Conflict,
                "The version token sent with this change is not one this server issued.",
                nameof(SetMediaEditsRequest.ExpectedRowVersion));
        }

        var saved = await SaveAsync(item, cancellationToken);

        if (saved.IsSuccess)
        {
            logger.LogInformation(
                "Media item {MediaItemId} was edited; edits version is now {EditsVersion}, so every " +
                "rendition URL for it has changed.",
                item.Id,
                item.EditsVersion);
        }

        return saved;
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDetail>> RevertEditsAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaDetail>("Editing media is not permitted.");
        }

        var item = await context.MediaItems.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<MediaDetail>(id);

        // Always a write, even on an item that carries no edits. The counter has to move so that
        // every URL emitted while the edits were in force stops resolving — reverting an image and
        // leaving the cropped version in CDN caches would look exactly like the revert not working.
        ApplyEdits(item, MediaEdits.None);

        var saved = await SaveAsync(item, cancellationToken);

        if (saved.IsSuccess)
        {
            logger.LogInformation(
                "Media item {MediaItemId} was reverted to its original; edits version is now " +
                "{EditsVersion}.",
                item.Id,
                item.EditsVersion);
        }

        return saved;
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaDeleteResult>("Deleting media is not permitted.");
        }

        var item = await context.MediaItems.FirstOrDefaultAsync(
            candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<MediaDeleteResult>(id);

        item.IsDeleted = true;
        item.DeletedOn = clock.GetUtcNow();
        item.DeletedBy = users.UserId;

        // A recycled item resolves to nothing and renders as the spec section 15.3 placeholder, so
        // every page showing it has changed.
        cacheInvalidation.EnqueueMedia(id);

        // The indexer will find the query filter hiding it and remove its document.
        search.EnqueueMedia(id);

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Media item {MediaItemId} was moved to the recycle bin.", id);

        // The bytes stay exactly where they are. A soft delete that removed the object would make
        // restore a lie, and the deduplication index is filtered on IsDeleted so re-uploading the
        // same file lands on the same content-addressed key regardless.
        return CmsResult<MediaDeleteResult>.Success(new MediaDeleteResult(id, WasPermanent: false, 0));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaUpload))
        {
            return Forbidden<MediaDetail>("Restoring media is not permitted.");
        }

        var item = await context.MediaItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<MediaDetail>(id);

        if (!item.IsDeleted)
        {
            return CmsResult<MediaDetail>.Invalid(
                MediaCodes.NotDeleted,
                "This item is not in the recycle bin, so there is nothing to restore.");
        }

        item.IsDeleted = false;
        item.DeletedOn = null;
        item.DeletedBy = null;

        // The deduplication index is unique over live rows only, so restoring an item whose bytes
        // were uploaded again while it sat in the bin collides here. Refusing is the honest answer:
        // the file the editor wanted is already in the library under the newer id.
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            logger.LogInformation(
                exception,
                "Restoring media item {MediaItemId} collided with an item holding the same bytes.",
                id);

            return CmsResult<MediaDetail>.Conflict(
                MediaCodes.Duplicate,
                "The same file was uploaded again while this item was in the recycle bin, so it " +
                "cannot be restored. Use the newer item instead.");
        }

        logger.LogInformation("Media item {MediaItemId} was restored from the recycle bin.", id);

        return CmsResult<MediaDetail>.Success(MediaProjections.ToDetail(item));
    }

    /// <inheritdoc />
    public async Task<CmsResult<MediaDeleteResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.MediaDelete))
        {
            return Forbidden<MediaDeleteResult>("Permanently deleting media is not permitted.");
        }

        var item = await context.MediaItems
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (item is null) return NotFound<MediaDeleteResult>(id);

        if (!item.IsDeleted)
        {
            return CmsResult<MediaDeleteResult>.Invalid(
                MediaCodes.NotDeleted,
                "Move the item to the recycle bin first. Permanent deletion is not undoable, so it " +
                "is never the first thing that happens to a file.");
        }

        var whereUsed = await impact.WhereUsedAsync(ContentReferenceTargetType.Media, id, cancellationToken);

        if (whereUsed.IsReferenced)
        {
            logger.LogInformation(
                "Permanent deletion of media item {MediaItemId} was refused; {PageCount} page(s) and " +
                "{ItemCount} reusable item(s) still show it.",
                id,
                whereUsed.AffectedPageCount,
                whereUsed.AffectedReusableItems.Count);

            return CmsResult<MediaDeleteResult>.Conflict(MediaCodes.StillReferenced, Describe(whereUsed));
        }

        var renditions = await context.MediaRenditions
            .Where(rendition => rendition.MediaItemId == id)
            .ToListAsync(cancellationToken);

        context.MediaRenditions.RemoveRange(renditions);
        context.MediaItems.Remove(item);

        // Rows first, objects second — the mirror image of the upload's ordering and for the mirror
        // image of the reason. An object with no row is storage a sweep reclaims; a row with no
        // object is a broken image on every page that shows it, and the SaveChanges is the step that
        // can still fail.
        await context.SaveChangesAsync(cancellationToken);

        // Renditions are this item's alone — the spec they are keyed by names it — so they go
        // unconditionally. The original does not: the deduplication index is unique over *live* rows
        // only, so a file binned, re-uploaded, and binned again leaves two rows sharing one
        // content-addressed key. Deleting the object out from under the survivor would turn a purge
        // of one item into a broken image on another.
        foreach (var key in renditions.Select(rendition => rendition.StorageKey))
        {
            await store.DeleteAsync(key, cancellationToken);
        }

        var sharedWith = await context.MediaItems
            .IgnoreQueryFilters()
            .CountAsync(other => other.StorageKey == item.StorageKey, cancellationToken);

        if (sharedWith is 0)
        {
            await store.DeleteAsync(item.StorageKey, cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "Stored original {StorageKey} was kept; {Count} other item(s) still point at it.",
                item.StorageKey,
                sharedWith);
        }

        logger.LogWarning(
            "Media item {MediaItemId} was permanently deleted along with {RenditionCount} rendition(s).",
            id,
            renditions.Count);

        return CmsResult<MediaDeleteResult>.Success(
            new MediaDeleteResult(id, WasPermanent: true, renditions.Count));
    }

    /// <inheritdoc />
    public async Task<CmsResult<ReferenceImpact>> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        authorization.HasPermission(CmsPermissions.ContentRead)
            ? CmsResult<ReferenceImpact>.Success(
                await impact.WhereUsedAsync(ContentReferenceTargetType.Media, id, cancellationToken))
            : Forbidden<ReferenceImpact>("Reading the media library is not permitted.");

    /// <summary>
    /// Writes a new edit document and advances the generation counter.
    /// </summary>
    /// <param name="item">The tracked item.</param>
    /// <param name="edits">The edits to store.</param>
    /// <remarks>
    /// The focal point is written to its own columns as well as into the document. The document is
    /// what the renderer reads — one source of truth for the pixels — and the columns are what the
    /// picker sorts and filters on without parsing JSON in SQL.
    /// </remarks>
    private static void ApplyEdits(MediaItem item, MediaEdits edits)
    {
        item.EditsJson = edits.ToJson();
        item.EditsVersion++;
        item.FocalPointX = edits.FocalPoint?.X;
        item.FocalPointY = edits.FocalPoint?.Y;
    }

    /// <summary>Saves a tracked item, mapping a lost concurrency race to a conflict.</summary>
    private async Task<CmsResult<MediaDetail>> SaveAsync(MediaItem item, CancellationToken cancellationToken)
    {
        // Every library-scope write goes through here — metadata, edits, a revert, a restore — and
        // every one of them changes what a page showing this item renders: the alt text in the
        // markup, or the URL of every rendition when the edits generation moves. One enqueue here
        // rather than four at the call sites is what keeps a later fifth writer from forgetting
        // (task P8-09, spec section 16.2).
        cacheInvalidation.EnqueueMedia(item.Id);

        // And its search document, for the same reason: file name, alt text, caption and credit are
        // exactly what somebody searches the library by (task P8-18).
        search.EnqueueMedia(item.Id);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The stored row comes back with the refusal, so the losing editor can show what they
            // lost to rather than being told to reload and find out (spec section 11.8).
            context.ChangeTracker.Clear();

            var stored = await context.MediaItems
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(candidate => candidate.Id == item.Id, cancellationToken);

            return CmsResult<MediaDetail>.Conflict(
                MediaCodes.Conflict,
                "Somebody else changed this item while you were editing it.",
                path: null,
                stored is null ? null : MediaProjections.ToDetail(stored));
        }

        return CmsResult<MediaDetail>.Success(MediaProjections.ToDetail(item));
    }

    /// <summary>Renders a where-used result as the sentence a refusal shows.</summary>
    private static string Describe(ReferenceImpact whereUsed)
    {
        var pages = whereUsed.AffectedPageCount switch
        {
            0 => null,
            1 => "1 page",
            var count => $"{count} pages",
        };

        var items = whereUsed.AffectedReusableItems.Count switch
        {
            0 => null,
            1 => "1 reusable item",
            var count => $"{count} reusable items",
        };

        var subject = string.Join(" and ", new[] { pages, items }.Where(part => part is not null));

        return $"{subject} still show this file. Replace those usages before deleting it permanently.";
    }

    /// <summary>Checks metadata against the columns that store it.</summary>
    private static CmsResult<MediaDetail>? TooLong(
        string? altText,
        string? title,
        string? caption,
        string? credit)
    {
        if (altText is { Length: > FieldLengths.ShortDescription })
        {
            return Invalid("The alternative text is too long.", nameof(PatchMediaRequest.AltText));
        }

        if (title is { Length: > FieldLengths.EntityName })
        {
            return Invalid("The title is too long.", nameof(PatchMediaRequest.Title));
        }

        if (caption is { Length: > FieldLengths.Caption })
        {
            return Invalid("The caption is too long.", nameof(PatchMediaRequest.Caption));
        }

        return credit is { Length: > FieldLengths.EntityName }
            ? Invalid("The credit is too long.", nameof(PatchMediaRequest.Credit))
            : null;

        static CmsResult<MediaDetail> Invalid(string message, string path) =>
            CmsResult<MediaDetail>.Invalid(MediaCodes.TooLong, message, path);
    }

    /// <summary>An empty string is a cleared field rather than a field holding whitespace.</summary>
    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static CmsResult<T> Forbidden<T>(string message) =>
        CmsResult<T>.Forbidden(message, MediaCodes.Forbidden);

    private static CmsResult<T> NotFound<T>(int id) =>
        CmsResult<T>.NotFound($"Media item {id} does not exist.", MediaCodes.NotFound);

    private static CmsResult<T> NotFound<T>(string message) =>
        CmsResult<T>.NotFound(message, MediaCodes.NotFound);
}
