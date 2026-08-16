using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Library;

/// <summary>
/// Everything an editor does to a media item that is not uploading it (tasks P5-10, P5-23, P5-24,
/// spec sections 13.4 and 13.8).
/// </summary>
/// <remarks>
/// The read and metadata half of the library. It deliberately does not touch bytes: the pipeline in
/// <c>IMediaUploadService</c> owns everything that sniffs, scans, and stores, so there is exactly
/// one path by which content enters the store and exactly one place the refusals live.
/// <para>
/// <strong>Edits here are non-destructive without exception.</strong> Rotating, cropping, or
/// re-pointing the focal point writes a JSON document and increments a counter; the original bytes
/// are the ones that were uploaded and stay that way, which is what makes revert free and what makes
/// a mistaken crop a nuisance rather than a restore from backup (spec section 13.4).
/// </para>
/// </remarks>
public interface IMediaLibraryService
{
    /// <summary>
    /// Browses the library.
    /// </summary>
    /// <param name="query">Filters and paging.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>One page of matching items, with the total the pager needs.</returns>
    Task<CmsResult<MediaListResult>> ListAsync(
        MediaQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one item's metadata and edits.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The item, or a not-found result.</returns>
    Task<CmsResult<MediaDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the editor-maintained metadata, leaving omitted members alone.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The members to change.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated item, or why it was refused.</returns>
    Task<CmsResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the item's library-scope edits and bumps its edits generation.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The complete edit document.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated item, or why it was refused.</returns>
    /// <remarks>
    /// The generation is incremented even when the document is unchanged in substance, because the
    /// counter's job is to make every URL for this item different from every URL that came before —
    /// and "the editor pressed save twice" is indistinguishable, from a cache's point of view, from
    /// "the editor changed their mind twice".
    /// </remarks>
    Task<CmsResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards the library-scope edits, returning the item to the bytes that were uploaded.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The reverted item, or a not-found result.</returns>
    Task<CmsResult<MediaDetail>> RevertEditsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an item to the recycle bin.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What happened, or a not-found result.</returns>
    /// <remarks>
    /// Not blocked by references, deliberately, and the asymmetry with <see cref="PurgeAsync"/> is
    /// the whole design of spec section 13.8: a soft delete is reversible, so the right answer to
    /// "this is still on twelve pages" is to hide the item and let the editor discover those pages,
    /// rather than to refuse an action they can undo.
    /// </remarks>
    Task<CmsResult<MediaDeleteResult>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings an item back out of the recycle bin.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The restored item, or why it was refused.</returns>
    Task<CmsResult<MediaDetail>> RestoreAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an item, its renditions, and its bytes for good.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    /// <returns>What was removed, or a conflict carrying the where-used list.</returns>
    /// <remarks>
    /// Refused while any <c>ContentReference</c> row points at the item, and refused unless it is
    /// already in the bin (task P5-24, spec section 13.8). The guard is a query rather than a
    /// constraint because both ends of that table are polymorphic, and the indexer over-reports
    /// rather than under-reports — so this refusal is occasionally more cautious than it strictly
    /// needs to be, which is the correct direction for an irreversible operation.
    /// </remarks>
    Task<CmsResult<MediaDeleteResult>> PurgeAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers which pages and reusable items show this media item.
    /// </summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The impact, or a forbidden result.</returns>
    Task<CmsResult<ReferenceImpact>> WhereUsedAsync(int id, CancellationToken cancellationToken = default);
}
