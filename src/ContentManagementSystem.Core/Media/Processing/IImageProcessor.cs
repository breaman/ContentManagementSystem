using ContentManagementSystem.Core.Media.Upload;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>What an image's header says, read without decoding its pixels.</summary>
/// <param name="Width">Encoded width in pixels, before orientation is applied.</param>
/// <param name="Height">Encoded height in pixels, before orientation is applied.</param>
/// <param name="Format">The container format the decoder recognised.</param>
/// <param name="Rotation">Clockwise rotation the EXIF orientation asks for, in degrees.</param>
/// <param name="Mirrored">Whether the EXIF orientation also mirrors the image.</param>
/// <remarks>
/// Header-only, and that is the entire point of the type. The decode-bomb guard has to know how
/// large an image claims to be <em>before</em> anything allocates a buffer for it
/// (spec section 13.3 step 4).
/// </remarks>
public sealed record ImageProbe(
    int Width,
    int Height,
    MediaByteFormat Format,
    int Rotation = 0,
    bool Mirrored = false)
{
    /// <summary>Total pixel count, as the decode-bomb guard measures it.</summary>
    /// <remarks><see cref="long"/> because the product of two ints is exactly what overflows here.</remarks>
    public long PixelCount => (long)Width * Height;

    /// <summary>Dimensions once the orientation has been applied.</summary>
    /// <remarks>
    /// A quarter turn swaps them, which is why a portrait photograph from a phone reports landscape
    /// dimensions until this is applied — and why every downstream size calculation would be wrong
    /// if the orientation were left in a flag rather than baked into the pixels.
    /// </remarks>
    public PixelSize OrientedSize => Rotation is 90 or 270
        ? new PixelSize(Height, Width)
        : new PixelSize(Width, Height);
}

/// <summary>An encoded image produced by the processor.</summary>
/// <param name="Bytes">The encoded file.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="Format">Format it was encoded as.</param>
/// <param name="MimeType">Media type matching <paramref name="Format"/>.</param>
public sealed record ProcessedImage(
    byte[] Bytes,
    int Width,
    int Height,
    ImageOutputFormat Format,
    string MimeType);

/// <summary>
/// Decoding, editing, and encoding images (task P5-09, spec section 13.9).
/// </summary>
/// <remarks>
/// One implementation ships — <see cref="SkiaSharpImageProcessor"/> — and the abstraction is kept
/// anyway. It costs almost nothing and it is what makes the AVIF limitation recoverable: a
/// Magick.NET implementation, or a libavif binding used for encoding only, drops in behind this
/// without touching the rendition, delivery, or caching layers (ADR 0011).
/// <para>
/// <see cref="SupportedOutputFormats"/> is the reason the abstraction is not merely decorative. Skia
/// answers a request to encode AVIF with <see langword="null"/> rather than an exception, so a
/// pipeline that trusted the enum would produce empty files and log nothing. The set is declared
/// here and asserted at startup, which turns that silent failure into a refusal to boot.
/// </para>
/// </remarks>
public interface IImageProcessor
{
    /// <summary>Formats this implementation can actually encode.</summary>
    IReadOnlySet<ImageOutputFormat> SupportedOutputFormats { get; }

    /// <summary>
    /// Verifies the declared capabilities against the running native library.
    /// </summary>
    /// <exception cref="InvalidOperationException">A declared format cannot in fact be encoded.</exception>
    /// <remarks>
    /// Called once at startup. It encodes a tiny image in each declared format and checks that bytes
    /// came back, which is the only honest test of a native encoder's presence — the managed enum
    /// lists formats the build may not include.
    /// </remarks>
    void AssertCapabilities();

    /// <summary>
    /// Reads an image's dimensions and orientation without decoding it.
    /// </summary>
    /// <param name="content">A seekable stream over the encoded image.</param>
    /// <returns>What the header says, or null when it is not a decodable image.</returns>
    ImageProbe? Probe(Stream content);

    /// <summary>
    /// Produces the original to store: orientation baked into the pixels, all metadata gone.
    /// </summary>
    /// <param name="content">A seekable stream over the uploaded image.</param>
    /// <param name="probe">What <see cref="Probe"/> reported.</param>
    /// <returns>The bytes to store, or null when the image could not be decoded.</returns>
    /// <remarks>
    /// Both halves matter. Baking the orientation means every later consumer — the picker, the crop
    /// editor, the rendition arithmetic — sees an upright image and none of them needs to know EXIF
    /// exists. Stripping the metadata is a privacy control: GPS coordinates in a published
    /// photograph are an incident, and the stored original is what a "download original" action
    /// hands out (spec section 13.9.1).
    /// </remarks>
    ProcessedImage? NormalizeOriginal(Stream content, ImageProbe probe);

    /// <summary>
    /// Renders one rendition.
    /// </summary>
    /// <param name="content">A seekable stream over the stored original.</param>
    /// <param name="spec">The rendition asked for, carrying any usage-level geometry.</param>
    /// <param name="libraryEdits">The item's own edits, applied before the usage-level ones.</param>
    /// <returns>The encoded rendition, or null when the source could not be decoded.</returns>
    /// <remarks>
    /// Operations apply in a fixed order — the library's rotate, flip, and crop, then the usage
    /// crop, then the mode's own fit — because a crop is recorded against what the editor was
    /// looking at. Applying them in another order silently moves every crop on the site.
    /// </remarks>
    ProcessedImage? Render(Stream content, RenditionSpec spec, MediaEdits libraryEdits);
}
