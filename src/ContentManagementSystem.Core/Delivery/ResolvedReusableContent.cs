using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// Why a placement of a reusable item did not resolve to anything to render.
/// </summary>
/// <remarks>
/// Every member is an ordinary condition rather than an error, and every one of them renders nothing
/// (spec section 15.3). They are told apart because the log line is the only place an editor's
/// broken-references report gets its detail from, and "unpublished" and "deleted" have different
/// remedies.
/// </remarks>
public enum ReusableResolutionStatus
{
    /// <summary>A version was found and can be rendered.</summary>
    Resolved = 0,

    /// <summary>No such item, or it is in the recycle bin.</summary>
    NotFound = 1,

    /// <summary>The item exists but has never been published, or has been retired.</summary>
    NotPublished = 2,

    /// <summary>
    /// The placement pins a version that no longer exists, or that belongs to a different item.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="NotFound"/> because the item is fine and the <em>page</em> is
    /// wrong: the remedy is the "update to latest" action on the placement, not anything done to the
    /// item (spec section 9.2).
    /// </remarks>
    PinnedVersionMissing = 3,

    /// <summary>The placement closes a loop back to an item already being rendered.</summary>
    Cycle = 4,

    /// <summary>The nesting is deeper than the delivery path will follow.</summary>
    TooDeep = 5,
}

/// <summary>
/// One reusable item, loaded and ready to be rendered as the block type it is shaped by
/// (task P4-06, spec section 9.2).
/// </summary>
/// <remarks>
/// The reusable-content counterpart of <see cref="PublishedContent"/>, and immutable for the same
/// reasons: it holds no entities and no database context, so a renderer cannot walk from the
/// fragment it is rendering to the rest of the library, and it is safe to cache.
/// </remarks>
/// <param name="Status">Whether this is something to render, and if not, why not.</param>
/// <param name="ReusableContentId">The item's identity, and the <c>ru:{id}</c> cache tag's subject.</param>
/// <param name="Key">The item's stable key, for diagnostics.</param>
/// <param name="Name">The item's display name, shown by the editor's badge in preview.</param>
/// <param name="VersionId">Identity of the version being rendered.</param>
/// <param name="VersionNumber">That version's number within the item.</param>
/// <param name="IsPinned">Whether the placement named this version rather than following the item.</param>
/// <param name="IsLatest">
/// Whether the version being rendered is the one currently published. False on a pinned placement
/// that has fallen behind, which is what the editor's "update to latest" affordance keys off
/// (task P4-05).
/// </param>
/// <param name="PublishedVersionNumber">
/// The version an unpinned placement would render, or null while the item has none.
/// </param>
/// <param name="BlockTypeKey">Block type the item is shaped by, which selects the component.</param>
/// <param name="BlockTypeRevision">Revision the version's payload was authored against.</param>
/// <param name="Payload">
/// The version's content. Its <c>zones</c> object holds the block's properties — see
/// <c>ReusableContentSchema</c> for why a block's properties are stored as a payload's zones.
/// </param>
/// <param name="Schema">
/// The captured property definitions of that revision, or null when it could not be resolved. Null
/// is a rendering condition rather than an error: the stored values carry their own field type
/// discriminators, and only the configuration is lost.
/// </param>
public sealed record ResolvedReusableContent(
    ReusableResolutionStatus Status,
    int ReusableContentId,
    string Key,
    string Name,
    int VersionId,
    int VersionNumber,
    bool IsPinned,
    bool IsLatest,
    int? PublishedVersionNumber,
    string BlockTypeKey,
    int BlockTypeRevision,
    ContentPayload? Payload,
    BlockTypeSchema? Schema)
{
    /// <summary>Whether there is content to render.</summary>
    public bool IsResolved => Status is ReusableResolutionStatus.Resolved && Payload is not null;

    /// <summary>
    /// The block's stored properties, or an undefined element when nothing resolved.
    /// </summary>
    /// <remarks>
    /// Undefined rather than null so that the renderer's property reads behave exactly as they do
    /// for a block whose properties member is missing — one shape for "there is nothing here",
    /// rather than a null check the block component would have to remember.
    /// </remarks>
    public JsonElement Properties => Payload?.Zones ?? default;

    /// <summary>A placement that resolved to nothing, carrying only why.</summary>
    /// <param name="status">Why it did not resolve.</param>
    /// <param name="reusableContentId">The item the placement named.</param>
    /// <returns>The unresolved result.</returns>
    public static ResolvedReusableContent Unresolved(ReusableResolutionStatus status, int reusableContentId) =>
        new(
            status,
            reusableContentId,
            string.Empty,
            string.Empty,
            0,
            0,
            false,
            false,
            null,
            string.Empty,
            0,
            null,
            null);
}
