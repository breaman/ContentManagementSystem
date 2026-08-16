using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Library;

/// <summary>
/// The media library's organizing tree (task P5-23, spec sections 13.1 and 23.3).
/// </summary>
/// <remarks>
/// Organization and nothing else. A folder is not part of any address and not part of any storage
/// key, so moving an item between folders changes no URL and breaks no page — which is the property
/// that makes filing a decision an editor can revise rather than one they have to get right first
/// time.
/// <para>
/// The tree is the same shape as the content tree: a nullable parent and a materialized path of
/// ids. "Everything under this folder" is then a prefix match on an indexed column rather than a
/// recursive query, and a rename touches one row.
/// </para>
/// </remarks>
public interface IMediaFolderService
{
    /// <summary>
    /// Reads the whole folder tree.
    /// </summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The folders at the root, each carrying its descendants.</returns>
    /// <remarks>
    /// The whole tree in one call rather than a level at a time. A media library's folder count is
    /// bounded by how many an editor will navigate, and lazily expanding branches would trade one
    /// small query for a round trip per click on a control whose whole job is to be quick.
    /// </remarks>
    Task<CmsResult<IReadOnlyList<MediaFolderNode>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a folder.
    /// </summary>
    /// <param name="request">Name and parent.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The new folder, or why it was refused.</returns>
    Task<CmsResult<MediaFolderNode>> CreateAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames, reorders, or moves a folder, rewriting the paths of everything beneath it.
    /// </summary>
    /// <param name="id">Identity of the folder.</param>
    /// <param name="request">The members to change.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated folder, or why it was refused.</returns>
    /// <remarks>
    /// A move into the folder's own subtree is refused. Allowing it would detach the branch from the
    /// root while leaving every row present and every foreign key satisfied — the tree would simply
    /// be smaller than the table, and no query would report anything missing.
    /// </remarks>
    Task<CmsResult<MediaFolderNode>> PatchAsync(
        int id,
        PatchMediaFolderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an empty folder.
    /// </summary>
    /// <param name="id">Identity of the folder.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The folder's id, or why it was refused.</returns>
    /// <remarks>
    /// Refused while it holds items or child folders, rather than cascading. Deleting a folder is a
    /// filing decision; deleting the forty photographs inside it is not, and one gesture must not
    /// quietly mean both.
    /// </remarks>
    Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
