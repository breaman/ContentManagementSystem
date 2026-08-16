using System.Globalization;

using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Delivery;

/// <summary>The raw parts of a rendition request, as they appear in the URL.</summary>
/// <param name="MediaItemId">Item id from the path.</param>
/// <param name="Size">The <c>{width}x{height}</c> path segment.</param>
/// <param name="Mode">The mode path segment.</param>
/// <param name="FileName">The final path segment, whose extension names the format.</param>
/// <param name="EditsVersion">The <c>v</c> parameter.</param>
/// <param name="Quality">The <c>q</c> parameter.</param>
/// <param name="FocalPoint">The <c>f</c> parameter, as <c>x,y</c>.</param>
/// <param name="Crop">The <c>c</c> parameter, as <c>x,y,w,h</c>.</param>
public sealed record RenditionRequest(
    int MediaItemId,
    string? Size,
    string? Mode,
    string? FileName,
    string? EditsVersion = null,
    string? Quality = null,
    string? FocalPoint = null,
    string? Crop = null);

/// <summary>What parsing a rendition request produced.</summary>
/// <param name="Spec">The parsed spec, when the request was well formed and offered.</param>
/// <param name="FailureCode">A <see cref="MediaCodes"/> value, when it was not.</param>
/// <param name="FailureMessage">What to tell the caller.</param>
public readonly record struct RenditionParseResult(
    RenditionSpec? Spec,
    string? FailureCode = null,
    string? FailureMessage = null)
{
    /// <summary>Whether a spec was produced.</summary>
    public bool IsSuccess => Spec is not null;
}

/// <summary>
/// Turns a rendition URL into a spec, or refuses it (tasks P5-14 and P5-15,
/// spec section 13.5).
/// </summary>
/// <remarks>
/// Separated from the endpoint so the refusals are testable without a request, and so the
/// application's own URL building and its URL parsing are provably the inverse of each other.
/// <para>
/// <strong>AVIF is refused here, at the parsing layer.</strong> That placement is the point: the
/// image library cannot encode AVIF and answers a request to try with <see langword="null"/>, so a
/// request that reached the generator would produce a 200 with an empty body — a broken image that
/// looks like a successful response to every cache between here and the browser
/// (spec section 13.9.1).
/// </para>
/// </remarks>
public static class RenditionRequestParser
{
    /// <summary>
    /// Parses a request.
    /// </summary>
    /// <param name="request">The raw URL parts.</param>
    /// <returns>The spec, or the refusal.</returns>
    /// <remarks>
    /// Everything is checked before anything is generated: the size is on the allowlist, the mode
    /// and format are ones this site serves, and the geometry is inside the image. A spec that comes
    /// out of here is one the generator can act on without re-validating.
    /// </remarks>
    public static RenditionParseResult TryParse(RenditionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.MediaItemId <= 0) return Refused(MediaCodes.NotFound, "No such media item.");

        if (!TryParseSize(request.Size, out var width, out var height))
        {
            return Refused(MediaCodes.RenditionNotAllowed, "The requested size is not one this site serves.");
        }

        if (!TryParseMode(request.Mode, out var mode))
        {
            return Refused(MediaCodes.RenditionNotAllowed, "The requested mode is not one this site serves.");
        }

        var extension = Path.GetExtension(request.FileName ?? string.Empty).TrimStart('.').ToLowerInvariant();

        if (extension is "avif")
        {
            return Refused(
                MediaCodes.AvifNotSupported,
                "AVIF renditions are not produced by this site. Request WebP or the original format.");
        }

        if (!TryParseFormat(extension, out var format))
        {
            return Refused(MediaCodes.RenditionNotAllowed, "The requested format is not one this site serves.");
        }

        var quality = RenditionSpec.DefaultQuality;

        if (!string.IsNullOrEmpty(request.Quality) &&
            !int.TryParse(request.Quality, NumberStyles.None, CultureInfo.InvariantCulture, out quality))
        {
            return Refused(MediaCodes.RenditionNotAllowed, "The requested quality is not a number.");
        }

        var editsVersion = 0;

        if (!string.IsNullOrEmpty(request.EditsVersion) &&
            !int.TryParse(request.EditsVersion, NumberStyles.None, CultureInfo.InvariantCulture, out editsVersion))
        {
            return Refused(MediaCodes.RenditionNotAllowed, "The edits version is not a number.");
        }

        NormalizedPoint? focalPoint = null;

        if (!string.IsNullOrEmpty(request.FocalPoint))
        {
            if (!TryParseFractions(request.FocalPoint, 2, out var values))
            {
                return Refused(MediaCodes.RenditionNotAllowed, "The focal point is malformed.");
            }

            focalPoint = new NormalizedPoint(values[0], values[1]);
        }

        NormalizedRect? crop = null;

        if (!string.IsNullOrEmpty(request.Crop))
        {
            if (!TryParseFractions(request.Crop, 4, out var values))
            {
                return Refused(MediaCodes.RenditionNotAllowed, "The crop is malformed.");
            }

            crop = new NormalizedRect(values[0], values[1], values[2], values[3]);
        }

        var spec = new RenditionSpec(
            request.MediaItemId, width, height, mode, format, quality, editsVersion, focalPoint, crop);

        // The allowlist is the second guard behind the signature: the signature stops an outsider
        // asking for an arbitrary size, and this stops the application itself from doing so through
        // a template bug that would otherwise mint a thousand distinct encodes.
        return spec.IsAllowed
            ? new RenditionParseResult(spec)
            : Refused(MediaCodes.RenditionNotAllowed, "The requested rendition is not one this site serves.");
    }

    private static RenditionParseResult Refused(string code, string message) => new(null, code, message);

    private static bool TryParseSize(string? segment, out int width, out int height)
    {
        width = 0;
        height = 0;

        if (string.IsNullOrEmpty(segment)) return false;

        var separator = segment.IndexOf('x', StringComparison.Ordinal);

        if (separator <= 0 || separator == segment.Length - 1) return false;

        return int.TryParse(
                   segment.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out width) &&
               int.TryParse(
                   segment.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out height);
    }

    private static bool TryParseMode(string? segment, out RenditionMode mode)
    {
        mode = RenditionMode.Crop;

        return segment?.ToLowerInvariant() switch
        {
            "crop" => true,
            "contain" => Set(RenditionMode.Contain, out mode),
            "cover" => Set(RenditionMode.Cover, out mode),
            "pad" => Set(RenditionMode.Pad, out mode),
            _ => false,
        };
    }

    private static bool TryParseFormat(string extension, out ImageOutputFormat format)
    {
        format = ImageOutputFormat.Jpeg;

        return extension switch
        {
            "jpg" or "jpeg" => true,
            "png" => Set(ImageOutputFormat.Png, out format),
            "webp" => Set(ImageOutputFormat.Webp, out format),
            _ => false,
        };
    }

    /// <summary>
    /// Parses a comma-separated list of fractions of an exact length.
    /// </summary>
    /// <param name="value">The parameter value.</param>
    /// <param name="expected">How many numbers there must be.</param>
    /// <param name="fractions">The parsed values.</param>
    /// <returns><see langword="true"/> when the value is exactly that many fractions in 0–1.</returns>
    /// <remarks>
    /// Invariant culture, matching the signer. A server whose locale writes <c>0,5</c> would produce
    /// URLs no other server could read back, and the comma would collide with the separator besides.
    /// </remarks>
    private static bool TryParseFractions(string value, int expected, out double[] fractions)
    {
        fractions = [];

        var parts = value.Split(',');

        if (parts.Length != expected) return false;

        var parsed = new double[expected];

        for (var index = 0; index < expected; index++)
        {
            if (!double.TryParse(
                    parts[index], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fraction) ||
                fraction is < 0 or > 1)
            {
                return false;
            }

            parsed[index] = fraction;
        }

        fractions = parsed;

        return true;
    }

    private static bool Set<T>(T value, out T target)
    {
        target = value;

        return true;
    }
}
