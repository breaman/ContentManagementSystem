namespace ContentManagementSystem.Shared.Contracts.Media;

/// <summary>
/// One media item as the management API reports it (spec section 22.1).
/// </summary>
/// <param name="Id">Identity of the item.</param>
/// <param name="FolderId">Folder it is filed in, or null at the root of the library.</param>
/// <param name="FileName">Server-generated file name.</param>
/// <param name="OriginalFileName">The name the file was uploaded under, for display.</param>
/// <param name="ContentType">The sniffed media type it is served as.</param>
/// <param name="Kind">How the library classifies it — image, document, video, or audio.</param>
/// <param name="SizeBytes">Size of the stored original.</param>
/// <param name="Width">Pixel width, after orientation was baked in. Null for non-images.</param>
/// <param name="Height">Pixel height, after orientation was baked in. Null for non-images.</param>
/// <param name="AltText">Alternative text, or null when the item is decorative.</param>
/// <param name="IsDecorative">Whether the item renders <c>alt=""</c>.</param>
/// <param name="Title">Editor-facing title.</param>
/// <param name="Caption">Caption rendered alongside the image.</param>
/// <param name="Credit">Attribution line.</param>
/// <param name="FocalPointX">Horizontal focal point as a fraction of the width.</param>
/// <param name="FocalPointY">Vertical focal point as a fraction of the height.</param>
/// <param name="EditsVersion">Generation counter for the library-scope edits.</param>
/// <param name="UploadedOn">When the item was created.</param>
/// <remarks>
/// <strong>No URL and no storage key.</strong> A client addresses an item by id and gets its picture
/// through signed rendition URLs the server generates; handing out the storage key would expose the
/// content-addressed layout and invite requests that bypass the media endpoint (spec section 13.5).
/// </remarks>
public sealed record MediaDetail(
    int Id,
    int? FolderId,
    string FileName,
    string OriginalFileName,
    string ContentType,
    string Kind,
    long SizeBytes,
    int? Width,
    int? Height,
    string? AltText,
    bool IsDecorative,
    string? Title,
    string? Caption,
    string? Credit,
    double? FocalPointX,
    double? FocalPointY,
    int EditsVersion,
    DateTimeOffset? UploadedOn);

/// <summary>
/// What an upload did.
/// </summary>
/// <param name="Item">The item the upload resolved to.</param>
/// <param name="Deduplicated">
/// Whether identical bytes were already in the library, in which case <paramref name="Item"/> is the
/// existing item and nothing was stored.
/// </param>
/// <param name="SanitizationRemovals">
/// Elements and attributes an SVG lost to the strict profile. Empty for every other kind of file.
/// </param>
/// <remarks>
/// Deduplication is reported rather than hidden. The editor asked to add a file and got back one
/// that already existed under a different name, possibly in a different folder — telling them that
/// is the difference between "this is the same photo you uploaded in March" and an upload that
/// appears to have silently landed somewhere else (spec section 13.1).
/// </remarks>
public sealed record MediaUploadResult(
    MediaDetail Item,
    bool Deduplicated,
    IReadOnlyList<string> SanitizationRemovals);
