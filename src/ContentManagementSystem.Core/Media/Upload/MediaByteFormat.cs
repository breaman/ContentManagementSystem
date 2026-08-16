namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// What a file's leading bytes say it actually is (spec section 13.3 step 3).
/// </summary>
/// <remarks>
/// A container format rather than a MIME type, because that is what bytes can honestly answer. A
/// <c>.docx</c> and a <c>.xlsx</c> are both <see cref="Zip"/> and nothing in the first kilobyte
/// distinguishes them; claiming otherwise would mean either rejecting valid files or inventing a
/// certainty the sniffer does not have. The allowlist maps each accepted extension to the format its
/// bytes must be, so the pair is checked without either half pretending to be more precise than it
/// is.
/// </remarks>
public enum MediaByteFormat
{
    /// <summary>Nothing recognised. Everything not on the allowlist lands here, including HTML.</summary>
    Unknown = 0,

    /// <summary>JPEG.</summary>
    Jpeg,

    /// <summary>PNG.</summary>
    Png,

    /// <summary>GIF, either 87a or 89a.</summary>
    Gif,

    /// <summary>WebP, in its RIFF container.</summary>
    Webp,

    /// <summary>
    /// AVIF. Recognised specifically in order to be refused.
    /// </summary>
    /// <remarks>
    /// Detected rather than left as <see cref="Unknown"/> so the refusal can say why. SkiaSharp
    /// cannot encode AVIF, so an accepted AVIF upload would be an item whose renditions silently
    /// fail to generate forever afterwards (spec section 13.9.1).
    /// </remarks>
    Avif,

    /// <summary>SVG — an XML document whose root element is <c>svg</c>.</summary>
    Svg,

    /// <summary>PDF.</summary>
    Pdf,

    /// <summary>A ZIP container, which is what OOXML documents are.</summary>
    Zip,

    /// <summary>An ISO base media file, which is what MP4 is.</summary>
    Mp4,
}
