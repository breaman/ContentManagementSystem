using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// One file being added to the library.
/// </summary>
/// <param name="Content">
/// The uploaded bytes. Must be seekable — the pipeline reads them several times, to sniff, to scan,
/// to hash, and to store.
/// </param>
/// <param name="FileName">The client-supplied file name. Untrusted; used only for its extension.</param>
/// <param name="FolderId">Folder to file the item in, or null for the root of the library.</param>
/// <param name="AltText">Alternative text for an image.</param>
/// <param name="IsDecorative">Whether the image carries no information and renders <c>alt=""</c>.</param>
/// <param name="Title">Editor-facing title.</param>
/// <param name="Caption">Caption rendered alongside the image.</param>
/// <param name="Credit">Attribution line.</param>
public sealed record MediaUploadRequest(
    Stream Content,
    string FileName,
    int? FolderId = null,
    string? AltText = null,
    bool IsDecorative = false,
    string? Title = null,
    string? Caption = null,
    string? Credit = null);

/// <summary>
/// The upload pipeline (tasks P5-05 to P5-07, spec section 13.3).
/// </summary>
/// <remarks>
/// Ten ordered steps, and the order is the design rather than an implementation detail. Cheap
/// refusals come before expensive work: a file is measured before it is read, sniffed before it is
/// decoded, and its dimensions are checked before a single pixel is allocated. Anything that costs
/// real resources happens only to bytes that have already survived everything free.
/// </remarks>
public interface IMediaUploadService
{
    /// <summary>
    /// Runs an upload through the pipeline.
    /// </summary>
    /// <param name="request">The file and the metadata to record against it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The stored — or already existing — item, or why the upload was refused.</returns>
    Task<CmsResult<MediaUploadResult>> UploadAsync(
        MediaUploadRequest request,
        CancellationToken cancellationToken = default);
}
