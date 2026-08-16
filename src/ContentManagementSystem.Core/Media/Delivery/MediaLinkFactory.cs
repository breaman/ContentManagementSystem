using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Delivery;

/// <summary>
/// Signs the URLs the backoffice shows an item at (task P5-22, spec section 13.5).
/// </summary>
/// <remarks>
/// The backoffice is a client like any other: it never constructs a signature and never sees a
/// storage key, so its thumbnails come from here. Two sizes rather than one because the two screens
/// want different things — a grid of a hundred items must not fetch a hundred full-size renditions,
/// and a detail panel showing a 320 px thumbnail scaled up would be the wrong picture to judge a
/// crop by.
/// </remarks>
public static class MediaLinkFactory
{
    /// <summary>Width of the grid thumbnail, from the allowlist.</summary>
    public const int ThumbnailWidth = 320;

    /// <summary>Width of the detail-panel preview, from the allowlist.</summary>
    public const int PreviewWidth = 960;

    /// <summary>
    /// Builds the URL set for one item.
    /// </summary>
    /// <param name="item">The item as the API reports it.</param>
    /// <param name="signer">Signs each URL.</param>
    /// <returns>The URLs a client may fetch it from.</returns>
    /// <remarks>
    /// The rendition URLs are null for anything the pipeline cannot produce — a document, an SVG, an
    /// item whose dimensions were never recorded — and the original is always present. A client that
    /// received a rendition URL for a PDF would show a broken image where a download link belongs.
    /// </remarks>
    public static MediaLinks For(MediaDetail item, IMediaUrlSigner signer)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(signer);

        return new MediaLinks(
            item.Id,
            Rendition(item, signer, ThumbnailWidth),
            Rendition(item, signer, PreviewWidth),
            signer.BuildOriginalUrl(item.Id, item.EditsVersion, item.OriginalFileName));
    }

    /// <summary>
    /// Signs one rendition of an item, at its own aspect ratio.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="signer">Signs the URL.</param>
    /// <param name="width">Requested width, which must be on the allowlist.</param>
    /// <returns>The URL, or null when the item has no rendition pipeline.</returns>
    /// <remarks>
    /// At the item's own ratio, and with the library edits applied to the dimensions first — so the
    /// thumbnail of a photograph the editor rotated is portrait, exactly as the pages showing it
    /// are. Requesting a square would crop every landscape picture in the grid and make the library
    /// harder to search by eye than the filenames it replaced.
    /// </remarks>
    private static string? Rendition(MediaDetail item, IMediaUrlSigner signer, int width)
    {
        if (!string.Equals(item.Kind, nameof(MediaKind.Image), StringComparison.Ordinal)) return null;

        if (ResponsiveImages.FallbackFormat(item.ContentType) is null) return null;

        var edits = item.Edits ?? MediaEdits.None;

        if (MediaGeometry.Effective(item.Width, item.Height, edits) is not { } size) return null;

        var height = (int)Math.Round((double)width * size.Height / size.Width);

        if (height <= 0) return null;

        return signer.BuildUrl(
            new RenditionSpec(
                item.Id,
                width,
                height,
                RenditionMode.Contain,
                ImageOutputFormat.Webp,
                RenditionSpec.DefaultQuality,
                item.EditsVersion,
                edits.FocalPoint),
            item.OriginalFileName);
    }
}
