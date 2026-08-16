using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>
/// How large a picture actually is once every edit has been applied to it, and how to read an
/// aspect-ratio restriction (tasks P5-19 and P5-20, spec sections 13.4 and 13.6).
/// </summary>
/// <remarks>
/// Two callers need the same arithmetic and would otherwise each invent it. The renderer needs the
/// effective size to write accurate <c>width</c> and <c>height</c> attributes — the whole point of
/// which is that the browser reserves the right box before the bytes arrive, and a rotated or
/// cropped item whose <em>stored</em> dimensions were emitted would reserve the wrong one
/// (spec section 13.6). The publish check needs it to judge <c>minWidth</c> and <c>aspectRatio</c>
/// against what the page will show rather than against what was uploaded.
/// <para>
/// The order mirrors <c>SkiaSharpImageProcessor.Render</c> exactly — rotate, flip, library crop,
/// usage crop — because the two must agree about what a placement resolves to. They are separate
/// because this one is integer arithmetic that runs on every render, and decoding an image to ask it
/// how big it is would be an absurd price for a number the row already holds.
/// </para>
/// </remarks>
public static class MediaGeometry
{
    /// <summary>
    /// The dimensions a placement of an item resolves to.
    /// </summary>
    /// <param name="width">Pixel width of the stored original, after orientation was baked in.</param>
    /// <param name="height">Pixel height of the stored original.</param>
    /// <param name="libraryEdits">The item's library-scope edits, which every usage inherits.</param>
    /// <param name="usageCrop">This placement's own crop, or null when it takes the whole picture.</param>
    /// <returns>The effective size, or null when the item has no dimensions to start from.</returns>
    /// <remarks>
    /// A flip is absent from the arithmetic on purpose: mirroring an image does not change how large
    /// it is. A quarter-turn does, which is the case this exists for.
    /// </remarks>
    public static PixelSize? Effective(
        int? width,
        int? height,
        MediaEdits libraryEdits,
        NormalizedRect? usageCrop = null)
    {
        ArgumentNullException.ThrowIfNull(libraryEdits);

        if (width is not { } sourceWidth || height is not { } sourceHeight) return null;
        if (sourceWidth <= 0 || sourceHeight <= 0) return null;

        var size = libraryEdits.Rotate is 90 or 270
            ? new PixelSize(sourceHeight, sourceWidth)
            : new PixelSize(sourceWidth, sourceHeight);

        if (libraryEdits.Crop is { } libraryCrop)
        {
            var region = RenditionGeometry.ToPixels(size, libraryCrop);

            size = new PixelSize(region.Width, region.Height);
        }

        if (usageCrop is { } crop)
        {
            var region = RenditionGeometry.ToPixels(size, crop);

            size = new PixelSize(region.Width, region.Height);
        }

        return size;
    }

    /// <summary>
    /// Reads an <c>aspectRatio</c> setting.
    /// </summary>
    /// <param name="value">The configured text, such as <c>16:9</c>.</param>
    /// <param name="ratio">Width divided by height, when the text could be read.</param>
    /// <returns><see langword="true"/> when the setting names a usable ratio.</returns>
    /// <remarks>
    /// <strong>The syntax settled in P5, as the field configuration schema promised.</strong> It is
    /// <c>W:H</c> with whole or fractional parts — <c>16:9</c>, <c>1:1</c>, <c>4:3</c> — and a bare
    /// decimal is also accepted, because <c>1.7778</c> is what a crop editor computes and refusing it
    /// would mean the editor and the configuration spoke different languages about the same number.
    /// <para>
    /// Both sides must be positive. A ratio of zero or a negative one describes no rectangle, and
    /// treating it as unset would silently drop a restriction somebody wrote.
    /// </para>
    /// </remarks>
    public static bool TryParseAspectRatio(string? value, out double ratio)
    {
        ratio = 0;

        if (string.IsNullOrWhiteSpace(value)) return false;

        var separator = value.IndexOfAny([':', '/']);

        if (separator < 0)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out ratio) &&
                ratio > 0;
        }

        if (!double.TryParse(
                value.AsSpan(0, separator),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var numerator) ||
            !double.TryParse(
                value.AsSpan(separator + 1),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var denominator) ||
            numerator <= 0 ||
            denominator <= 0)
        {
            return false;
        }

        ratio = numerator / denominator;

        return true;
    }

    /// <summary>
    /// How much a measured ratio may differ from a required one and still count as matching.
    /// </summary>
    /// <remarks>
    /// One percent. A crop is stored as fractions and applied to whole pixels, so a 16:9 crop of a
    /// 3000×2000 photograph lands a pixel or two off the exact ratio every time; a check with no
    /// tolerance would refuse to publish crops the editor made with the tool this restriction exists
    /// to drive. One percent is far tighter than the eye and far looser than rounding.
    /// </remarks>
    public const double AspectRatioTolerance = 0.01;

    /// <summary>Whether a size satisfies a required aspect ratio.</summary>
    /// <param name="size">The effective size of the placement.</param>
    /// <param name="ratio">The required ratio, width divided by height.</param>
    /// <returns><see langword="true"/> when the two agree within <see cref="AspectRatioTolerance"/>.</returns>
    public static bool MatchesAspectRatio(PixelSize size, double ratio)
    {
        if (size.Width <= 0 || size.Height <= 0 || ratio <= 0) return false;

        var actual = (double)size.Width / size.Height;

        return Math.Abs(actual - ratio) <= ratio * AspectRatioTolerance;
    }

    /// <summary>Formats a ratio the way the configuration writes it, for a diagnostic message.</summary>
    /// <param name="size">The size to describe.</param>
    /// <returns>The ratio as a decimal, to two places.</returns>
    public static string DescribeRatio(PixelSize size) =>
        size.Height <= 0
            ? "0"
            : ((double)size.Width / size.Height).ToString("0.##", CultureInfo.InvariantCulture);
}
