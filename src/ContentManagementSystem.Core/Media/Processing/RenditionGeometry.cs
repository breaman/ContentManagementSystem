using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>A rectangle in whole pixels.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height);

/// <summary>A size in whole pixels.</summary>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct PixelSize(int Width, int Height);

/// <summary>
/// The rendition arithmetic: which pixels of the source a rendition uses, and how big the output is
/// (task P5-12, spec sections 13.4 and 13.5).
/// </summary>
/// <remarks>
/// Pure arithmetic on integers and fractions, with no reference to SkiaSharp. That is deliberate:
/// this is the part with the off-by-one errors and the aspect-ratio mistakes in it, and keeping it
/// free of the imaging library means it can be unit-tested exhaustively without a single native
/// dependency or a single decoded image (task P5-26).
/// </remarks>
public static class RenditionGeometry
{
    /// <summary>
    /// Turns a normalized rectangle into pixels of a particular source.
    /// </summary>
    /// <param name="source">Size of the image the fractions are relative to.</param>
    /// <param name="rect">The normalized rectangle.</param>
    /// <returns>The rectangle in pixels, clamped inside the source and at least one pixel each way.</returns>
    /// <remarks>
    /// Clamping matters more than it looks. Rounding each edge independently can produce a rectangle
    /// a pixel wider than the image it came from, and a decoder handed that either throws or returns
    /// a garbage row — a rare, size-dependent failure that turns up on one image out of a thousand.
    /// </remarks>
    public static PixelRect ToPixels(PixelSize source, NormalizedRect rect)
    {
        var x = Clamp((int)Math.Round(rect.X * source.Width), 0, Math.Max(source.Width - 1, 0));
        var y = Clamp((int)Math.Round(rect.Y * source.Height), 0, Math.Max(source.Height - 1, 0));
        var width = Clamp((int)Math.Round(rect.Width * source.Width), 1, source.Width - x);
        var height = Clamp((int)Math.Round(rect.Height * source.Height), 1, source.Height - y);

        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// Chooses the region of a source to show at a requested aspect ratio, keeping a point visible.
    /// </summary>
    /// <param name="source">Size of the source, after any explicit crop has been applied.</param>
    /// <param name="target">Size the rendition was asked for.</param>
    /// <param name="focalPoint">Point to keep visible, or null to centre.</param>
    /// <returns>The region of the source to scale into the target.</returns>
    /// <remarks>
    /// The largest rectangle of the requested aspect ratio that fits in the source, positioned so
    /// the focal point sits where it does in the source — then slid back inside the bounds when that
    /// would hang off an edge. Sliding rather than shrinking is what makes a focal point near a
    /// corner behave sensibly: the subject moves as far towards the corner as it can and the
    /// rendition stays full-bleed, rather than the crop quietly getting smaller and the image
    /// getting softer.
    /// </remarks>
    public static PixelRect FocalCrop(PixelSize source, PixelSize target, NormalizedPoint? focalPoint)
    {
        if (source.Width <= 0 || source.Height <= 0 || target.Width <= 0 || target.Height <= 0)
        {
            return new PixelRect(0, 0, Math.Max(source.Width, 1), Math.Max(source.Height, 1));
        }

        var targetAspect = (double)target.Width / target.Height;
        var sourceAspect = (double)source.Width / source.Height;

        int cropWidth;
        int cropHeight;

        if (sourceAspect > targetAspect)
        {
            // The source is wider than the box: full height, trim the sides.
            cropHeight = source.Height;
            cropWidth = Math.Max(1, (int)Math.Round(source.Height * targetAspect));
        }
        else
        {
            // Taller than the box: full width, trim top and bottom.
            cropWidth = source.Width;
            cropHeight = Math.Max(1, (int)Math.Round(source.Width / targetAspect));
        }

        // Rounding can push the computed side one pixel past the source; the crop is never allowed
        // to be larger than what it is cut from.
        cropWidth = Math.Min(cropWidth, source.Width);
        cropHeight = Math.Min(cropHeight, source.Height);

        var focal = focalPoint ?? NormalizedPoint.Center;
        var focalX = focal.X * source.Width;
        var focalY = focal.Y * source.Height;

        var x = Clamp((int)Math.Round(focalX - (cropWidth / 2.0)), 0, source.Width - cropWidth);
        var y = Clamp((int)Math.Round(focalY - (cropHeight / 2.0)), 0, source.Height - cropHeight);

        return new PixelRect(x, y, cropWidth, cropHeight);
    }

    /// <summary>
    /// Works out how large the output actually is for a mode.
    /// </summary>
    /// <param name="source">Size of the region being rendered.</param>
    /// <param name="requested">Size the caller asked for.</param>
    /// <param name="mode">How the image fills the box.</param>
    /// <returns>The dimensions of the produced image.</returns>
    /// <remarks>
    /// <see cref="RenditionMode.Pad"/> returns the requested box exactly, and
    /// <see cref="RenditionMode.Crop"/> returns it whenever the source can fill it — that is what
    /// makes them safe to write into an <c>&lt;img&gt;</c>'s <c>width</c> and <c>height</c>, which is
    /// what protects Cumulative Layout Shift (spec section 13.6). <see cref="RenditionMode.Contain"/>
    /// returns whatever fits, so the renderer has to read the result rather than assume it.
    /// <para>
    /// <strong>No image is ever enlarged.</strong> A 400 px original asked for at 1280 px comes back
    /// at 400: upscaling produces a blurrier file that is several times bigger, and does it silently.
    /// Padding is the one mode whose <em>canvas</em> keeps the requested size, because the space it
    /// adds is empty rather than invented pixels.
    /// </para>
    /// </remarks>
    public static PixelSize Resolve(PixelSize source, PixelSize requested, RenditionMode mode)
    {
        if (source.Width <= 0 || source.Height <= 0) return new PixelSize(1, 1);

        return mode switch
        {
            // Exactly the box that was asked for. Padding is by definition empty space around an
            // image, so a source too small to fill the box is the ordinary case rather than a reason
            // to shrink it — the image inside is still never upscaled, because the contained size is
            // computed separately.
            RenditionMode.Pad => requested,

            // The requested box — unless the source cannot fill it, in which case the whole box
            // shrinks by the limiting factor. Scaling the box rather than clamping one side is what
            // keeps the requested aspect ratio intact: clamping width alone would hand back a
            // differently-shaped image than the markup reserved space for.
            RenditionMode.Crop => ScaleSize(requested, Math.Min(
                1.0,
                Math.Min((double)source.Width / requested.Width, (double)source.Height / requested.Height))),

            RenditionMode.Cover => ScaleSize(source, Math.Min(
                1.0,
                Math.Max((double)requested.Width / source.Width, (double)requested.Height / source.Height))),

            // Contain, and the fallback for anything unrecognised: never larger than the source and
            // never larger than the box.
            _ => ScaleSize(source, Math.Min(
                1.0,
                Math.Min((double)requested.Width / source.Width, (double)requested.Height / source.Height))),
        };
    }

    private static PixelSize ScaleSize(PixelSize size, double factor) => new(
        Math.Max(1, (int)Math.Round(size.Width * factor)),
        Math.Max(1, (int)Math.Round(size.Height * factor)));

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
