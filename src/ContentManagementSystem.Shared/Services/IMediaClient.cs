using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// A file on its way to the library, as a backoffice screen hands it over (tasks P5-08 and P5-22).
/// </summary>
/// <param name="Content">
/// The bytes, read from the current position. Read once and forward only, so a browser file stream
/// can be handed over without being buffered first.
/// </param>
/// <param name="FileName">The name the file is being uploaded under.</param>
/// <param name="SizeBytes">
/// How large the file is. Known before the bytes are read — a browser reports it from the file
/// system — and it is what decides whether the upload is sent in one request or in parts.
/// </param>
/// <param name="FolderId">Folder to file the item in, or null for the root of the library.</param>
/// <param name="AltText">Alternative text describing the image.</param>
/// <param name="IsDecorative">Whether the image carries no information and renders <c>alt=""</c>.</param>
/// <param name="Title">Editor-facing title.</param>
/// <param name="Caption">Caption rendered alongside the image.</param>
/// <param name="Credit">Attribution line.</param>
public sealed record MediaUploadContent(
    Stream Content,
    string FileName,
    long SizeBytes,
    int? FolderId = null,
    string? AltText = null,
    bool IsDecorative = false,
    string? Title = null,
    string? Caption = null,
    string? Credit = null);

/// <summary>
/// What the media admin screens and the media picker need from the server (tasks P5-19 and P5-22).
/// </summary>
/// <remarks>
/// Implemented twice, exactly as <see cref="IPageClient"/> and <see cref="IReusableClient"/> are:
/// over <c>HttpClient</c> in the WebAssembly backoffice, and directly over the services on the
/// server so a screen pre-renders with real content rather than a spinner.
/// <para>
/// Reads return bare values and writes return <see cref="StructureClientResult{T}"/>, following the
/// asymmetry the other clients set out. The case that makes it matter here is permanent deletion:
/// the refusal carries the reason a file could not be removed, and that reason <em>is</em> what the
/// screen has to put in front of the editor beside the where-used list (spec section 13.8).
/// </para>
/// <para>
/// <strong>Signed URLs are fetched, never built.</strong> There is no method here that turns an id
/// into a picture locally, because a client cannot sign one — that is the whole point of the
/// signature (spec section 13.5).
/// </para>
/// </remarks>
public interface IMediaClient
{
    /// <summary>Browses the library.</summary>
    /// <param name="query">Filters and paging.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<MediaListResult> ListAsync(MediaQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads one item's metadata and library-scope edits.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<MediaDetail?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the signed URLs a batch of items can be shown at.
    /// </summary>
    /// <param name="ids">The items to show.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <remarks>
    /// Batched because a grid of thumbnails is one screen and must not be one request per tile. An
    /// id with no entry in the result is an item that no longer exists or is in a bin the caller is
    /// not looking at.
    /// </remarks>
    Task<IReadOnlyDictionary<int, MediaLinks>> LinksAsync(
        IEnumerable<int> ids,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the whole folder tree, with the item count in each folder.</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<MediaFolderNode>> FoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a folder.</summary>
    /// <param name="request">Name and parent.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<MediaFolderNode>> CreateFolderAsync(
        CreateMediaFolderRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads a file, in one request or in parts depending on its size.
    /// </summary>
    /// <param name="content">The file and the metadata to record against it.</param>
    /// <param name="progress">
    /// Receives the fraction transferred, between 0 and 1. Only a chunked upload can report progress
    /// meaningfully; a single-request upload reports 1 when it finishes, because a client cannot see
    /// inside a request it has already handed to the browser.
    /// </param>
    /// <param name="cancellationToken">Token observed while uploading.</param>
    /// <returns>What the upload produced, deduplication included, or why it was refused.</returns>
    Task<StructureClientResult<MediaUploadResult>> UploadAsync(
        MediaUploadContent content,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>Puts new bytes behind an existing item, keeping its id and every page pointing at it.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="content">The new file.</param>
    /// <param name="cancellationToken">Token observed while uploading.</param>
    Task<StructureClientResult<MediaUploadResult>> ReplaceAsync(
        int id,
        MediaUploadContent content,
        CancellationToken cancellationToken = default);

    /// <summary>Updates the editor-maintained metadata, leaving omitted members alone.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The members to change.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<MediaDetail>> PatchAsync(
        int id,
        PatchMediaRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies library-scope geometry, which bumps the item's edits generation.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The complete edit document.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<MediaDetail>> SetEditsAsync(
        int id,
        SetMediaEditsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Discards the library-scope edits, returning the item to the bytes that were uploaded.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<MediaDetail>> RevertEditsAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Moves an item to the recycle bin.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<MediaDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Brings an item back out of the recycle bin.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<MediaDetail>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes an item and its bytes for good. Refused while any content shows it.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    Task<StructureClientResult<MediaDeleteResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Answers which pages and reusable items show this file.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<ReferenceImpact> WhereUsedAsync(int id, CancellationToken cancellationToken = default);
}
