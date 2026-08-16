using System.Text;

namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// Decides what a file is from its leading bytes (task P5-05, spec section 13.3 step 3).
/// </summary>
/// <remarks>
/// The upload pipeline's central safety check. An extension and a declared content type are both
/// strings the uploader chose; the bytes are not. Accepting a file because it is called
/// <c>photo.jpg</c> is what lets an HTML document be stored and later served from the site's own
/// origin, where it runs with the site's cookies and its CSP (spec section 20.7).
/// <para>
/// <strong>Recognition is positive, never by elimination.</strong> A format is returned only when
/// its signature is present; anything else is <see cref="MediaByteFormat.Unknown"/> and is refused.
/// The inverse — looking for markers of hostile content and accepting what lacks them — has to
/// anticipate every hostile format, and misses the next one.
/// </para>
/// </remarks>
public static class MediaTypeSniffer
{
    /// <summary>
    /// How many leading bytes the sniffer needs.
    /// </summary>
    /// <remarks>
    /// Generous for the SVG case, which is the only format here that is not identified by a fixed
    /// signature: an XML declaration, a doctype, a licence comment, and leading whitespace can all
    /// sit ahead of the root element. Every binary format is decided inside the first sixteen bytes.
    /// </remarks>
    public const int HeaderBytes = 1024;

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static ReadOnlySpan<byte> Gif87Signature => "GIF87a"u8;

    private static ReadOnlySpan<byte> Gif89Signature => "GIF89a"u8;

    private static ReadOnlySpan<byte> RiffSignature => "RIFF"u8;

    private static ReadOnlySpan<byte> WebpSignature => "WEBP"u8;

    private static ReadOnlySpan<byte> PdfSignature => "%PDF-"u8;

    private static ReadOnlySpan<byte> ZipSignature => [0x50, 0x4B, 0x03, 0x04];

    private static ReadOnlySpan<byte> IsoBoxType => "ftyp"u8;

    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Identifies the format of a file from its header.
    /// </summary>
    /// <param name="header">The first <see cref="HeaderBytes"/> bytes, or the whole file if shorter.</param>
    /// <returns>The recognised format, or <see cref="MediaByteFormat.Unknown"/>.</returns>
    /// <example>
    /// <code>
    /// var format = MediaTypeSniffer.Detect(header);          // MediaByteFormat.Jpeg
    /// var matches = format == descriptor.Format;             // false for HTML renamed .jpg
    /// </code>
    /// </example>
    public static MediaByteFormat Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegSignature)) return MediaByteFormat.Jpeg;

        if (header.StartsWith(PngSignature)) return MediaByteFormat.Png;

        if (header.StartsWith(Gif87Signature) || header.StartsWith(Gif89Signature)) return MediaByteFormat.Gif;

        if (header.StartsWith(PdfSignature)) return MediaByteFormat.Pdf;

        if (header.StartsWith(ZipSignature)) return MediaByteFormat.Zip;

        if (IsRiff(header, WebpSignature)) return MediaByteFormat.Webp;

        if (TryReadIsoBrand(header, out var brand)) return ClassifyIsoBrand(brand);

        return LooksLikeSvg(header) ? MediaByteFormat.Svg : MediaByteFormat.Unknown;
    }

    /// <summary>
    /// Reads the leading bytes of a stream without consuming it.
    /// </summary>
    /// <param name="content">A seekable stream positioned anywhere.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Up to <see cref="HeaderBytes"/> bytes from the start.</returns>
    /// <remarks>
    /// Rewinds the stream afterwards so callers can hand the same stream on to the hasher and the
    /// store. Seekability is guaranteed by the pipeline, which buffers the upload before any of this
    /// runs — every step here needs to read the content more than once.
    /// </remarks>
    public static async Task<byte[]> ReadHeaderAsync(Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        content.Position = 0;

        var buffer = new byte[HeaderBytes];
        var read = await content.ReadAtLeastAsync(
            buffer, HeaderBytes, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

        content.Position = 0;

        return read == HeaderBytes ? buffer : buffer[..read];
    }

    /// <summary>Checks a RIFF container's four-character form type.</summary>
    /// <param name="header">The file header.</param>
    /// <param name="form">The expected form type, such as <c>WEBP</c>.</param>
    /// <returns><see langword="true"/> when the header is that RIFF form.</returns>
    private static bool IsRiff(ReadOnlySpan<byte> header, ReadOnlySpan<byte> form) =>
        header.Length >= 12 && header.StartsWith(RiffSignature) && header[8..12].SequenceEqual(form);

    /// <summary>
    /// Reads the major brand of an ISO base media file.
    /// </summary>
    /// <param name="header">The file header.</param>
    /// <param name="brand">The four-character brand, when present.</param>
    /// <returns><see langword="true"/> when the header is an ISO base media file.</returns>
    /// <remarks>
    /// MP4 and AVIF share this container — the brand at offset 8 is the only thing separating a
    /// video this CMS accepts from an image format it cannot re-encode.
    /// </remarks>
    private static bool TryReadIsoBrand(ReadOnlySpan<byte> header, out ReadOnlySpan<byte> brand)
    {
        brand = default;

        if (header.Length < 12 || !header[4..8].SequenceEqual(IsoBoxType)) return false;

        brand = header[8..12];

        return true;
    }

    private static MediaByteFormat ClassifyIsoBrand(ReadOnlySpan<byte> brand)
    {
        // The AVIF brands, recognised so the refusal can name the real reason rather than reporting
        // an unknown file (spec section 13.9.1).
        if (brand.SequenceEqual("avif"u8) || brand.SequenceEqual("avis"u8)) return MediaByteFormat.Avif;

        // The MP4 brand family. "isom" and the "mp4x" pair cover standard files; "M4V " is what
        // several consumer tools still write. Anything else sharing this container is not something
        // to accept on the strength of the container alone.
        if (brand.SequenceEqual("isom"u8) || brand.SequenceEqual("iso2"u8) ||
            brand.SequenceEqual("mp41"u8) || brand.SequenceEqual("mp42"u8) ||
            brand.SequenceEqual("avc1"u8) || brand.SequenceEqual("M4V "u8))
        {
            return MediaByteFormat.Mp4;
        }

        return MediaByteFormat.Unknown;
    }

    /// <summary>
    /// Reports whether a header is an XML document whose root element is <c>svg</c>.
    /// </summary>
    /// <param name="header">The file header.</param>
    /// <returns><see langword="true"/> when the file opens as an SVG.</returns>
    /// <remarks>
    /// Deliberately strict about what may precede the root: a byte-order mark, whitespace, one XML
    /// declaration, one doctype, and comments. Nothing else. A file that opens <c>&lt;html&gt;</c>
    /// and mentions <c>&lt;svg&gt;</c> later is an HTML document — a scan for "does this contain
    /// &lt;svg" would accept it, and the browser would then run it as HTML.
    /// </remarks>
    private static bool LooksLikeSvg(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(Utf8Bom)) header = header[Utf8Bom.Length..];

        // Only well-formed UTF-8 gets this far. An SVG that is not decodable text is not an SVG that
        // any sanitizer should be asked to parse.
        if (!TryDecodeUtf8(header, out var text)) return false;

        var remaining = text.AsSpan().TrimStart();

        while (!remaining.IsEmpty && remaining[0] is '<')
        {
            if (remaining.StartsWith("<svg", StringComparison.OrdinalIgnoreCase))
            {
                // The root element must actually end here — "<svgfoo" is a different element.
                return remaining.Length is 4 || remaining[4] is ' ' or '\t' or '\r' or '\n' or '>' or '/' or ':';
            }

            if (remaining.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) ||
                remaining.StartsWith("<!--", StringComparison.Ordinal))
            {
                var end = remaining.IndexOf('>');

                if (end < 0) return false;

                remaining = remaining[(end + 1)..].TrimStart();

                continue;
            }

            return false;
        }

        return false;
    }

    /// <summary>
    /// Decodes a header as UTF-8, tolerating a truncated final character.
    /// </summary>
    /// <param name="header">The bytes to decode.</param>
    /// <param name="text">The decoded text.</param>
    /// <returns><see langword="true"/> when the bytes are valid UTF-8.</returns>
    /// <remarks>
    /// The header is a fixed-size window into a longer file, so its last character may be cut in
    /// half. <see cref="Decoder"/> keeps that trailing fragment to itself rather than treating it as
    /// invalid, which a one-shot <see cref="Encoding.GetString(byte[])"/> with a throwing fallback
    /// would not.
    /// </remarks>
    private static bool TryDecodeUtf8(ReadOnlySpan<byte> header, out string text)
    {
        var decoder = Encoding.UTF8.GetDecoder();

        decoder.Fallback = DecoderFallback.ExceptionFallback;

        try
        {
            var buffer = new char[header.Length];
            var written = decoder.GetChars(header, buffer, flush: false);

            text = new string(buffer, 0, written);

            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;

            return false;
        }
    }
}
