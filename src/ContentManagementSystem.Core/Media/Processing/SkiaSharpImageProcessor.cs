using System.Collections.Frozen;

using ContentManagementSystem.Core.Media.Upload;

using Microsoft.Extensions.Logging;

using SkiaSharp;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>
/// The v1 image processor, over SkiaSharp (task P5-09, ADR 0011).
/// </summary>
/// <remarks>
/// Chosen on licensing grounds: MIT, against ImageSharp's split licence, which would require a paid
/// commercial licence for a closed-source deployment of unestablished revenue and — from v4 —
/// enforces it with a build-time key. The cost of that choice is two capability gaps, both handled
/// here rather than discovered in production:
/// <list type="number">
/// <item>
/// <description>
/// <strong>No AVIF encoding.</strong> <see cref="SupportedOutputFormats"/> says so, and
/// <see cref="AssertCapabilities"/> proves it at startup by encoding a test image in each declared
/// format. Skia returns <see langword="null"/> from an unsupported encode instead of throwing, so
/// without this the failure would be an empty HTTP response nobody logged.
/// </description>
/// </item>
/// <item>
/// <description>
/// <strong>Unreliable EXIF orientation.</strong> <see cref="SKCodec.EncodedOrigin"/> has known
/// defects across formats and platforms, so orientation is read with MetadataExtractor and falls
/// back to Skia rather than either being trusted alone (spec section 13.9.1).
/// </description>
/// </item>
/// </list>
/// <para>
/// The upside of the same design: Skia carries no metadata through an encode, so every rendition is
/// stripped of EXIF as a property of the pipeline rather than as a step somebody has to remember.
/// </para>
/// </remarks>
public sealed class SkiaSharpImageProcessor : IImageProcessor
{
    /// <summary>Pixel dimensions of the image used to prove an encoder exists.</summary>
    private const int CapabilityProbeSize = 4;

    /// <summary>
    /// The resampling used for every draw and every scale.
    /// </summary>
    /// <remarks>
    /// Mitchell cubic resampling: the usual choice for photographic downscaling — sharper than a
    /// plain bilinear filter, without the ringing a Catmull-Rom kernel produces on hard edges. Held
    /// in one field so a copy that draws at 1:1 and a scale that halves an image cannot end up using
    /// different filters and producing visibly different renditions of one picture.
    /// </remarks>
    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    private readonly ILogger<SkiaSharpImageProcessor> _logger;

    /// <summary>Creates the processor.</summary>
    /// <param name="logger">Logger.</param>
    public SkiaSharpImageProcessor(ILogger<SkiaSharpImageProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// WebP plus the two original formats, which is exactly what spec section 13.5 serves. GIF is
    /// absent as an <em>output</em> deliberately: Skia's GIF support is decode-only, and an animated
    /// GIF re-encoded through a still-image pipeline would come back as one frame.
    /// </remarks>
    public IReadOnlySet<ImageOutputFormat> SupportedOutputFormats { get; } = new[]
    {
        ImageOutputFormat.Jpeg,
        ImageOutputFormat.Png,
        ImageOutputFormat.Webp,
    }.ToFrozenSet();

    /// <inheritdoc />
    public void AssertCapabilities()
    {
        using var bitmap = new SKBitmap(CapabilityProbeSize, CapabilityProbeSize);

        bitmap.Erase(SKColors.White);

        foreach (var format in SupportedOutputFormats)
        {
            var encoded = Encode(bitmap, format, RenditionSpec.DefaultQuality);

            if (encoded is { Length: > 0 }) continue;

            // Fails the host's startup. A deployment whose native build cannot produce the formats
            // its markup will ask for is a deployment that serves empty images, and the moment to
            // find that out is before it takes traffic.
            throw new InvalidOperationException(
                $"The image library reports support for {format} but produced no bytes. " +
                "The deployed SkiaSharp native build cannot encode it; media renditions would " +
                "silently return empty responses.");
        }

        _logger.LogInformation(
            "Image processor capabilities verified: {Formats}.",
            string.Join(", ", SupportedOutputFormats));
    }

    /// <inheritdoc />
    public ImageProbe? Probe(Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        content.Position = 0;

        // Orientation is read first, and from a separate reader: MetadataExtractor parses the EXIF
        // block itself rather than relying on the codec's interpretation of it.
        var (rotation, mirrored) = ImageOrientation.Read(content, _logger);

        content.Position = 0;

        // Wrapped in a non-owning managed stream on purpose. SKCodec takes ownership of the SKStream
        // it is handed and disposes it, and the single-argument Create(Stream) overload wraps the
        // caller's stream in an owning one — so probing an upload would close the very stream the
        // next pipeline step reads. Passing disposeManagedStream: false keeps ownership here.
        using var wrapped = new SKManagedStream(content, disposeManagedStream: false);
        using var codec = SKCodec.Create(wrapped, out var result);

        if (codec is null)
        {
            _logger.LogDebug("Image header was not decodable: {Result}.", result);

            return null;
        }

        var info = codec.Info;

        if (info.Width <= 0 || info.Height <= 0) return null;

        if (rotation is 0 && !mirrored)
        {
            // Nothing said otherwise, so fall back to the codec's own view (spec section 13.9.1).
            (rotation, mirrored) = ImageOrientation.FromEncodedOrigin(codec.EncodedOrigin);
        }

        content.Position = 0;

        return new ImageProbe(info.Width, info.Height, MapFormat(codec.EncodedFormat), rotation, mirrored);
    }

    /// <inheritdoc />
    public ProcessedImage? NormalizeOriginal(Stream content, ImageProbe probe)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(probe);

        var format = probe.Format switch
        {
            MediaByteFormat.Png => ImageOutputFormat.Png,
            MediaByteFormat.Webp => ImageOutputFormat.Webp,
            MediaByteFormat.Jpeg => ImageOutputFormat.Jpeg,

            // GIF and SVG do not come through here at all — a GIF re-encoded by a still-image
            // pipeline loses its animation, and an SVG is text that the SVG sanitizer handles.
            _ => (ImageOutputFormat?)null,
        };

        if (format is not { } outputFormat) return null;

        using var decoded = Decode(content);

        if (decoded is null) return null;

        using var upright = ApplyOrientation(decoded, probe.Rotation, probe.Mirrored);

        // Re-encoded even when the orientation was already upright. That is what strips the metadata
        // — Skia carries none through an encode — and the alternative, "keep the original bytes when
        // no rotation was needed", would mean GPS coordinates survive on exactly the photographs
        // that happened to be taken level (spec section 13.3 step 8).
        var bytes = Encode(upright, outputFormat, QualityFor(outputFormat));

        if (bytes is null) return null;

        return new ProcessedImage(bytes, upright.Width, upright.Height, outputFormat, MimeTypeFor(outputFormat));
    }

    /// <inheritdoc />
    public ProcessedImage? Render(Stream content, RenditionSpec spec, MediaEdits libraryEdits)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(libraryEdits);

        if (!SupportedOutputFormats.Contains(spec.Format))
        {
            throw new NotSupportedException($"This processor cannot encode {spec.Format}.");
        }

        using var decoded = Decode(content);

        if (decoded is null) return null;

        // The library's edits first: the editor straightened and cropped the item once, and every
        // usage inherits that view of it.
        using var edited = ApplyEdits(decoded, libraryEdits);

        // Then the usage's own crop, which is expressed against the image the editor saw — that is,
        // against the library-edited result rather than against the stored original.
        using var scoped = spec.Crop is { } usageCrop ? Crop(edited, usageCrop) : Reference(edited);

        var focalPoint = spec.FocalPoint ?? libraryEdits.FocalPoint;
        var source = new PixelSize(scoped.Width, scoped.Height);
        var requested = new PixelSize(spec.Width, spec.Height);
        var output = RenditionGeometry.Resolve(source, requested, spec.Mode);

        using var fitted = Fit(scoped, source, output, spec.Mode, focalPoint);

        if (fitted is null) return null;

        var bytes = Encode(fitted, spec.Format, spec.Quality);

        return bytes is null
            ? null
            : new ProcessedImage(bytes, fitted.Width, fitted.Height, spec.Format, spec.MimeType);
    }

    /// <summary>Decodes an encoded image into a bitmap.</summary>
    /// <param name="content">A seekable stream over the encoded image.</param>
    /// <returns>The decoded bitmap, or null when it could not be decoded.</returns>
    private SKBitmap? Decode(Stream content)
    {
        content.Position = 0;

        // Non-owning, for the reason Probe gives: the pipeline decodes the same stream more than
        // once, and an overload that closes it turns the second read into an ObjectDisposedException.
        using var wrapped = new SKManagedStream(content, disposeManagedStream: false);

        // SKBitmap.Decode returns null rather than throwing on malformed input, which is the case
        // the callers above are written for: a file that got this far passed the sniffer and the
        // dimension guard, so an undecodable one here is corruption rather than an attack.
        var bitmap = SKBitmap.Decode(wrapped);

        if (bitmap is null)
        {
            _logger.LogWarning("An image that passed validation could not be decoded.");
        }

        return bitmap;
    }

    /// <summary>Applies an EXIF orientation to the pixels.</summary>
    /// <param name="source">The decoded image.</param>
    /// <param name="rotation">Clockwise rotation in degrees.</param>
    /// <param name="mirrored">Whether the image is also mirrored horizontally.</param>
    /// <returns>An upright bitmap, or the source when nothing needed doing.</returns>
    private static SKBitmap ApplyOrientation(SKBitmap source, int rotation, bool mirrored) =>
        rotation is 0 && !mirrored
            ? Reference(source)
            : Transform(source, rotation, mirrored ? FlipDirection.Horizontal : FlipDirection.None);

    /// <summary>Applies the library-scope edits in their fixed order.</summary>
    /// <param name="source">The decoded original.</param>
    /// <param name="edits">The edits to apply.</param>
    /// <returns>The edited bitmap.</returns>
    private static SKBitmap ApplyEdits(SKBitmap source, MediaEdits edits)
    {
        using var oriented = edits.Rotate is 0 && edits.Flip is FlipDirection.None
            ? Reference(source)
            : Transform(source, edits.Rotate, edits.Flip);

        return edits.Crop is { } crop ? Crop(oriented, crop) : Reference(oriented);
    }

    /// <summary>
    /// Rotates and mirrors a bitmap.
    /// </summary>
    /// <param name="source">The bitmap to transform.</param>
    /// <param name="rotation">Clockwise rotation in degrees — 0, 90, 180, or 270.</param>
    /// <param name="flip">Mirroring applied after the rotation.</param>
    /// <returns>A new bitmap holding the transformed pixels.</returns>
    /// <remarks>
    /// Done with a canvas transform rather than by moving pixels by hand: the matrix is one place to
    /// get right, and Skia's own sampling handles the quarter-turn cases exactly.
    /// </remarks>
    private static SKBitmap Transform(SKBitmap source, int rotation, FlipDirection flip)
    {
        var quarterTurn = rotation is 90 or 270;
        var width = quarterTurn ? source.Height : source.Width;
        var height = quarterTurn ? source.Width : source.Height;

        var target = new SKBitmap(new SKImageInfo(width, height, source.ColorType, source.AlphaType));

        using var canvas = new SKCanvas(target);

        canvas.Translate(width / 2f, height / 2f);
        canvas.RotateDegrees(rotation);

        canvas.Scale(
            flip is FlipDirection.Horizontal ? -1 : 1,
            flip is FlipDirection.Vertical ? -1 : 1);

        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0, Sampling);

        return target;
    }

    /// <summary>Cuts a normalized rectangle out of a bitmap.</summary>
    /// <param name="source">The bitmap to cut from.</param>
    /// <param name="crop">The rectangle, in fractions of the source.</param>
    /// <returns>A new bitmap holding the cropped region.</returns>
    private static SKBitmap Crop(SKBitmap source, NormalizedRect crop)
    {
        var rect = RenditionGeometry.ToPixels(new PixelSize(source.Width, source.Height), crop);

        return Extract(source, rect);
    }

    /// <summary>Cuts a pixel rectangle out of a bitmap.</summary>
    /// <param name="source">The bitmap to cut from.</param>
    /// <param name="rect">The region to keep.</param>
    /// <returns>A new bitmap holding the region.</returns>
    private static SKBitmap Extract(SKBitmap source, PixelRect rect)
    {
        var target = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, source.ColorType, source.AlphaType));

        // ExtractSubset shares the source's pixels rather than copying them, so the copy below is
        // what makes the result independent of a source that is about to be disposed.
        using var subset = new SKBitmap();

        if (!source.ExtractSubset(subset, new SKRectI(rect.X, rect.Y, rect.X + rect.Width, rect.Y + rect.Height)))
        {
            target.Dispose();

            return Reference(source);
        }

        using var canvas = new SKCanvas(target);

        canvas.DrawBitmap(subset, 0, 0, Sampling);

        return target;
    }

    /// <summary>
    /// Scales a bitmap into the resolved output size for a mode.
    /// </summary>
    /// <param name="source">The bitmap to fit.</param>
    /// <param name="sourceSize">Its dimensions.</param>
    /// <param name="output">The dimensions to produce.</param>
    /// <param name="mode">How the image fills the box.</param>
    /// <param name="focalPoint">Point to keep visible in <see cref="RenditionMode.Crop"/>.</param>
    /// <returns>The fitted bitmap, or null when the scale failed.</returns>
    private static SKBitmap? Fit(
        SKBitmap source,
        PixelSize sourceSize,
        PixelSize output,
        RenditionMode mode,
        NormalizedPoint? focalPoint)
    {
        if (mode is RenditionMode.Crop)
        {
            var region = RenditionGeometry.FocalCrop(sourceSize, output, focalPoint);

            using var cropped = Extract(source, region);

            return Resize(cropped, output);
        }

        if (mode is not RenditionMode.Pad) return Resize(source, output);

        // Pad returns the requested box exactly, with the image centred inside it. The fill is
        // transparent rather than a colour: a colour would be wrong against half the pages that use
        // it, and a caller who wants one can set a background in CSS over a transparent image.
        var contained = RenditionGeometry.Resolve(sourceSize, output, RenditionMode.Contain);

        using var scaled = Resize(source, contained);

        if (scaled is null) return null;

        var padded = new SKBitmap(new SKImageInfo(output.Width, output.Height, source.ColorType, SKAlphaType.Premul));

        using var canvas = new SKCanvas(padded);

        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(scaled, (output.Width - scaled.Width) / 2f, (output.Height - scaled.Height) / 2f, Sampling);

        return padded;
    }

    /// <summary>Scales a bitmap, or returns it unchanged when it is already the right size.</summary>
    /// <param name="source">The bitmap to scale.</param>
    /// <param name="size">The size to produce.</param>
    /// <returns>The scaled bitmap, or null when Skia refused the scale.</returns>
    private static SKBitmap? Resize(SKBitmap source, PixelSize size)
    {
        if (source.Width == size.Width && source.Height == size.Height) return Reference(source);

        return source.Resize(
            new SKImageInfo(size.Width, size.Height, source.ColorType, source.AlphaType),
            Sampling);
    }

    /// <summary>
    /// Copies a bitmap so the caller can dispose it independently of the source.
    /// </summary>
    /// <param name="source">The bitmap to copy.</param>
    /// <returns>An independent copy.</returns>
    /// <remarks>
    /// Every step in this pipeline hands its result to a <c>using</c> and returns a new bitmap, so a
    /// step that "does nothing" still has to produce something the caller may dispose. Returning the
    /// input would make a later disposal free pixels the next step is still reading.
    /// </remarks>
    private static SKBitmap Reference(SKBitmap source) => source.Copy();

    /// <summary>Encodes a bitmap.</summary>
    /// <param name="bitmap">The bitmap to encode.</param>
    /// <param name="format">The output format.</param>
    /// <param name="quality">Encoder quality.</param>
    /// <returns>The encoded bytes, or null when the encoder is not present.</returns>
    private static byte[]? Encode(SKBitmap bitmap, ImageOutputFormat format, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(ToSkiaFormat(format), quality);

        // Null is Skia's answer for "this build has no such encoder" — the AVIF trap. Callers treat
        // it as a failure rather than as empty content (spec section 13.9.1).
        return data?.ToArray();
    }

    private static SKEncodedImageFormat ToSkiaFormat(ImageOutputFormat format) => format switch
    {
        ImageOutputFormat.Png => SKEncodedImageFormat.Png,
        ImageOutputFormat.Webp => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Jpeg,
    };

    private static MediaByteFormat MapFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => MediaByteFormat.Jpeg,
        SKEncodedImageFormat.Png => MediaByteFormat.Png,
        SKEncodedImageFormat.Webp => MediaByteFormat.Webp,
        SKEncodedImageFormat.Gif => MediaByteFormat.Gif,
        SKEncodedImageFormat.Avif => MediaByteFormat.Avif,
        _ => MediaByteFormat.Unknown,
    };

    /// <summary>
    /// The quality a stored original is re-encoded at.
    /// </summary>
    /// <param name="format">The output format.</param>
    /// <returns>The encoder quality.</returns>
    /// <remarks>
    /// Higher than a rendition's, because this is the master every rendition is derived from and
    /// compression artefacts introduced here are inherited by all of them. PNG's parameter is a
    /// compression effort rather than a quality, and is lossless either way.
    /// </remarks>
    private static int QualityFor(ImageOutputFormat format) =>
        format is ImageOutputFormat.Png ? 100 : 92;

    private static string MimeTypeFor(ImageOutputFormat format) => format switch
    {
        ImageOutputFormat.Png => "image/png",
        ImageOutputFormat.Webp => "image/webp",
        _ => "image/jpeg",
    };
}
