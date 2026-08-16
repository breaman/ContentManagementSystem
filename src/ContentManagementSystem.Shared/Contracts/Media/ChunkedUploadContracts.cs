namespace ContentManagementSystem.Shared.Contracts.Media;

/// <summary>
/// Opens a resumable upload (task P5-08, spec section 13.3).
/// </summary>
/// <param name="FileName">The name the file is being uploaded under, for its extension and display.</param>
/// <param name="TotalBytes">
/// How large the whole file is. Declared up front so the size ceiling is applied before a single
/// byte is transferred rather than after all of them.
/// </param>
/// <param name="FolderId">Folder to file the item in, or null for the root of the library.</param>
/// <param name="AltText">Alternative text describing the image.</param>
/// <param name="IsDecorative">Whether the image carries no information and renders <c>alt=""</c>.</param>
/// <param name="Title">Editor-facing title.</param>
/// <param name="Caption">Caption rendered alongside the image.</param>
/// <param name="Credit">Attribution line.</param>
/// <remarks>
/// The metadata travels with the session rather than with the final part, so an upload interrupted
/// after the editor filled the form in does not lose what they typed.
/// </remarks>
public sealed record StartChunkedUploadRequest(
    string FileName,
    long TotalBytes,
    int? FolderId = null,
    string? AltText = null,
    bool IsDecorative = false,
    string? Title = null,
    string? Caption = null,
    string? Credit = null);

/// <summary>
/// Where a resumable upload has got to (task P5-08).
/// </summary>
/// <param name="UploadId">Server-generated identity of the session.</param>
/// <param name="FileName">The name the file is being uploaded under.</param>
/// <param name="TotalBytes">How large the whole file is.</param>
/// <param name="ReceivedBytes">How much of it the server holds.</param>
/// <param name="NextChunkIndex">
/// The part the server expects next. This is what makes the upload resumable: a client that lost its
/// connection asks for the session and continues from here, rather than starting again or guessing.
/// </param>
/// <param name="ChunkSize">The part size the server chose, in bytes.</param>
/// <param name="IsComplete">Whether every byte has arrived and the session is ready to finish.</param>
/// <param name="ExpiresOn">When an untouched session is swept and its fragments discarded.</param>
/// <remarks>
/// <see cref="ReceivedBytes"/> against <see cref="TotalBytes"/> is the progress report: it is the
/// server's own count of what it holds, not an echo of what the client believes it sent, which is
/// the difference between a progress bar and a decoration.
/// </remarks>
public sealed record ChunkedUploadSession(
    string UploadId,
    string FileName,
    long TotalBytes,
    long ReceivedBytes,
    int NextChunkIndex,
    int ChunkSize,
    bool IsComplete,
    DateTimeOffset ExpiresOn);
