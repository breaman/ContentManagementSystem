namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// What a media item is, decided from the sniffed bytes rather than from its extension
/// (spec section 23.3).
/// </summary>
/// <remarks>
/// Stored as the underlying <c>tinyint</c>. It is a classification of the stored file, not a
/// permission: an item is an <see cref="Image"/> because its bytes decode as one, which is why the
/// upload pipeline sets it after sniffing and nothing later may change it. The picker's
/// <c>allowedTypes</c> setting filters on this, and only <see cref="Image"/> items take part in the
/// rendition pipeline — asking for a 640px wide PDF is a request the delivery endpoint refuses
/// rather than one it tries to satisfy.
/// </remarks>
public enum MediaKind : byte
{
    /// <summary>A raster or vector image: JPEG, PNG, GIF, WebP, or SVG.</summary>
    Image = 0,

    /// <summary>A document: PDF, DOCX, or XLSX.</summary>
    Document = 1,

    /// <summary>A video file.</summary>
    Video = 2,

    /// <summary>An audio file.</summary>
    Audio = 3,
}
