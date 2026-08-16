using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Microsoft.Extensions.Logging;

using SkiaSharp;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>
/// Reads the orientation a camera recorded, so it can be baked into the pixels
/// (task P5-07, spec section 13.9.1).
/// </summary>
/// <remarks>
/// Two readers, in a deliberate order. <strong>MetadataExtractor is asked first</strong> because it
/// parses the EXIF block directly and is the more reliable of the two;
/// <see cref="SKCodec.EncodedOrigin"/> is the fallback, having documented defects across formats and
/// platforms. Trusting either alone produces sideways photographs on some subset of uploads, and the
/// subset differs between the two — which is precisely why both are consulted.
/// <para>
/// The eight EXIF orientation values are a rotation and an optional mirror. Only the rotation
/// matters for the common cases (a phone held sideways); the mirrored values come from front-facing
/// cameras and from scanners, and dropping them would flip those images.
/// </para>
/// </remarks>
public static class ImageOrientation
{
    /// <summary>
    /// Reads the orientation from a stream's metadata.
    /// </summary>
    /// <param name="content">A seekable stream over the encoded image.</param>
    /// <param name="logger">Logger for unreadable metadata.</param>
    /// <returns>The clockwise rotation in degrees, and whether the image is mirrored.</returns>
    /// <remarks>
    /// Unreadable metadata is not an error. A file with no EXIF block, or with a corrupt one, is an
    /// ordinary upload; it simply reports no rotation and lets the codec's own view be tried next.
    /// </remarks>
    public static (int Rotation, bool Mirrored) Read(Stream content, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var directories = ImageMetadataReader.ReadMetadata(content);
            var directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();

            if (directory is not null &&
                directory.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientation))
            {
                return FromExif(orientation);
            }
        }
        catch (ImageProcessingException exception)
        {
            logger.LogDebug(exception, "The image carried no readable metadata; falling back to the codec.");
        }
        catch (IOException exception)
        {
            logger.LogDebug(exception, "The image metadata could not be read; falling back to the codec.");
        }

        return (0, false);
    }

    /// <summary>
    /// Maps one of the eight EXIF orientation values.
    /// </summary>
    /// <param name="orientation">The EXIF tag value, 1 through 8.</param>
    /// <returns>The clockwise rotation in degrees, and whether the image is mirrored.</returns>
    /// <remarks>
    /// Anything outside 1–8 is treated as upright rather than rejected: an out-of-range orientation
    /// is a camera firmware bug, and refusing the photograph over it would be a worse answer than
    /// showing it the way it was stored.
    /// </remarks>
    public static (int Rotation, bool Mirrored) FromExif(int orientation) => orientation switch
    {
        2 => (0, true),
        3 => (180, false),
        4 => (180, true),
        5 => (90, true),
        6 => (90, false),
        7 => (270, true),
        8 => (270, false),
        _ => (0, false),
    };

    /// <summary>
    /// Maps the codec's own view of the orientation.
    /// </summary>
    /// <param name="origin">What <see cref="SKCodec.EncodedOrigin"/> reported.</param>
    /// <returns>The clockwise rotation in degrees, and whether the image is mirrored.</returns>
    public static (int Rotation, bool Mirrored) FromEncodedOrigin(SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.TopRight => (0, true),
        SKEncodedOrigin.BottomRight => (180, false),
        SKEncodedOrigin.BottomLeft => (180, true),
        SKEncodedOrigin.LeftTop => (90, true),
        SKEncodedOrigin.RightTop => (90, false),
        SKEncodedOrigin.RightBottom => (270, true),
        SKEncodedOrigin.LeftBottom => (270, false),
        _ => (0, false),
    };
}
