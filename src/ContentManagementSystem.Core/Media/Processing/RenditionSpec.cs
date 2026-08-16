using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>How a rendition fills the box it was asked for (spec section 13.5).</summary>
public enum RenditionMode
{
    /// <summary>Fill the box exactly, trimming what does not fit. Honours the focal point.</summary>
    Crop = 0,

    /// <summary>Fit inside the box, keeping the whole image. The result may be smaller than asked.</summary>
    Contain,

    /// <summary>Fill the box, overflowing on one axis. Differs from <see cref="Crop"/> in that nothing is trimmed.</summary>
    Cover,

    /// <summary>Fit inside the box and pad the remainder, so the result is exactly the requested size.</summary>
    Pad,
}

/// <summary>The formats a rendition may be produced in.</summary>
/// <remarks>
/// AVIF is absent, and its absence is asserted at startup rather than discovered at runtime:
/// SkiaSharp's AVIF encoder returns null instead of throwing, so a request for one would produce an
/// empty response body rather than an error (spec section 13.9.1).
/// </remarks>
public enum ImageOutputFormat
{
    /// <summary>JPEG.</summary>
    Jpeg = 0,

    /// <summary>PNG.</summary>
    Png,

    /// <summary>WebP — what modern browsers are served.</summary>
    Webp,
}

/// <summary>
/// One fully specified rendition: the identity of everything that affects the output bytes
/// (spec section 13.5).
/// </summary>
/// <param name="MediaItemId">Item being rendered.</param>
/// <param name="Width">Requested width in pixels.</param>
/// <param name="Height">Requested height in pixels.</param>
/// <param name="Mode">How the image fills that box.</param>
/// <param name="Format">Output format.</param>
/// <param name="Quality">Encoder quality.</param>
/// <param name="EditsVersion">The item's edits generation this was requested at.</param>
/// <param name="FocalPoint">Usage-level focal point, or null to use the item's own.</param>
/// <param name="Crop">Usage-level crop, or null to use the item's own.</param>
/// <remarks>
/// <strong>Everything that changes the bytes is in here, and the whole record is what gets
/// signed and hashed.</strong> That single property is doing three jobs at once: it is the cache
/// key, so two requests for the same image cannot produce two encodes; it is the signature payload,
/// so a tampered parameter fails validation rather than costing an encode; and because
/// <paramref name="EditsVersion"/> is part of it, a library edit changes every URL the site emits
/// and thereby busts browser and CDN caches with no purge to run (ADR 0007).
/// <para>
/// A parameter that affected the output but was left out of this record would break all three at
/// once — silently serving one variant's bytes for another's URL.
/// </para>
/// </remarks>
public sealed record RenditionSpec(
    int MediaItemId,
    int Width,
    int Height,
    RenditionMode Mode,
    ImageOutputFormat Format,
    int Quality,
    int EditsVersion,
    NormalizedPoint? FocalPoint = null,
    NormalizedRect? Crop = null)
{
    /// <summary>Default encoder quality.</summary>
    /// <remarks>
    /// 82 is the usual sweet spot for photographic content: visually indistinguishable from 95 at
    /// normal viewing sizes and roughly half the bytes.
    /// </remarks>
    public const int DefaultQuality = 82;

    /// <summary>Lowest quality a caller may ask for.</summary>
    public const int MinQuality = 30;

    /// <summary>Highest quality a caller may ask for.</summary>
    public const int MaxQuality = 95;

    /// <summary>
    /// Widths the delivery endpoint serves without further registration (spec section 13.5).
    /// </summary>
    /// <remarks>
    /// Belt and braces alongside the signature. The signature already makes an arbitrary size
    /// impossible for an outsider to request; the allowlist bounds what the application itself can
    /// ask for, so a bug in a template cannot turn one page into a thousand distinct encodes.
    /// </remarks>
    public static IReadOnlyList<int> AllowedWidths { get; } = [320, 640, 960, 1280, 1920, 2560];

    /// <summary>Whether every value is one the endpoint offers.</summary>
    /// <remarks>
    /// Height is bounded rather than allowlisted: an allowlist of heights would make every aspect
    /// ratio other than the few chosen ones unreachable, and the width allowlist plus the signature
    /// already bound the work a request can cause.
    /// </remarks>
    public bool IsAllowed =>
        MediaItemId > 0 &&
        AllowedWidths.Contains(Width) &&
        Height is > 0 and <= 4320 &&
        Quality is >= MinQuality and <= MaxQuality &&
        EditsVersion >= 0 &&
        FocalPoint is not { IsValid: false } &&
        Crop is not { IsValid: false };

    /// <summary>
    /// The canonical text of this spec — what is hashed and what is signed.
    /// </summary>
    /// <returns>A stable, culture-invariant rendering of every field.</returns>
    /// <remarks>
    /// Two properties matter and both are easy to lose. It is <em>culture-invariant</em>, because a
    /// server whose locale writes <c>0,5</c> would sign strings no other server validates. And the
    /// geometry is <em>rounded to four decimals</em>, because a focal point that arrived as
    /// <c>0.3333333333333333</c> on one request and <c>0.33333333333333331</c> on the next would key
    /// two rows and cost two encodes for the same picture.
    /// </remarks>
    public string ToCanonicalString()
    {
        var builder = new StringBuilder(128);

        builder.Append(MediaItemId).Append('|')
            .Append(Width).Append('x').Append(Height).Append('|')
            .Append(Mode.ToString().ToLowerInvariant()).Append('|')
            .Append(Format.ToString().ToLowerInvariant()).Append('|')
            .Append(Quality).Append('|')
            .Append('v').Append(EditsVersion).Append('|');

        if (FocalPoint is { } focal)
        {
            builder.Append('f').Append(Round(focal.X)).Append(',').Append(Round(focal.Y));
        }

        builder.Append('|');

        if (Crop is { } crop)
        {
            builder.Append('c')
                .Append(Round(crop.X)).Append(',')
                .Append(Round(crop.Y)).Append(',')
                .Append(Round(crop.Width)).Append(',')
                .Append(Round(crop.Height));
        }

        return builder.ToString();
    }

    /// <summary>SHA-256 of <see cref="ToCanonicalString"/>, used as the storage and lookup key.</summary>
    /// <returns>The 32-byte hash.</returns>
    public byte[] ComputeHash() => SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalString()));

    /// <summary>The file extension a rendition of this format is stored and served under.</summary>
    public string Extension => Format switch
    {
        ImageOutputFormat.Png => "png",
        ImageOutputFormat.Webp => "webp",
        _ => "jpg",
    };

    /// <summary>The media type a rendition of this format is served as.</summary>
    public string MimeType => Format switch
    {
        ImageOutputFormat.Png => "image/png",
        ImageOutputFormat.Webp => "image/webp",
        _ => "image/jpeg",
    };

    private static string Round(double value) =>
        Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
