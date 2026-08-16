using SkiaSharp;

namespace ContentManagementSystem.TestSupport;

/// <summary>
/// Builds the images the media tests need, including one carrying real EXIF.
/// </summary>
/// <remarks>
/// Generated rather than checked in as fixture files. A binary fixture is opaque in review — nobody
/// can see from the repository that <c>photo-with-gps.jpg</c> genuinely contains GPS coordinates,
/// which makes "the pipeline strips GPS" a test that could quietly stop proving anything the day the
/// fixture is replaced. Here the bytes that must be removed are written out in the test's own source.
/// </remarks>
public static class TestImages
{
    /// <summary>
    /// Encodes a plain image of the requested size.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="format">Format to encode as.</param>
    /// <returns>The encoded file.</returns>
    public static byte[] Encode(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(width, height);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.CornflowerBlue);

            // A shape rather than a flat fill, so a test that rotates the image can tell which way
            // up the result is.
            using var paint = new SKPaint { Color = SKColors.OrangeRed };

            canvas.DrawRect(0, 0, Math.Max(width / 4f, 1), Math.Max(height / 4f, 1), paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);

        return data.ToArray();
    }

    /// <summary>
    /// Encodes a JPEG carrying an EXIF block with an orientation and GPS coordinates.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="orientation">EXIF orientation value, 1 through 8.</param>
    /// <returns>The encoded file, with an <c>APP1</c> segment inserted after the start marker.</returns>
    /// <remarks>
    /// The GPS tag is what makes this fixture worth having: a published photograph carrying the
    /// coordinates of a private address is a real privacy incident, and it is the reason the
    /// pipeline re-encodes every stored original rather than only rotating the ones that need it
    /// (spec section 13.9.1).
    /// </remarks>
    public static byte[] EncodeWithExif(int width, int height, int orientation)
    {
        var jpeg = Encode(width, height);
        var app1 = BuildApp1(orientation);

        // Straight after the SOI marker, which is where a decoder expects application segments.
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }

    /// <summary>
    /// Builds an <c>APP1</c> segment holding a minimal little-endian TIFF with orientation and GPS.
    /// </summary>
    /// <param name="orientation">EXIF orientation value.</param>
    /// <returns>The complete segment, marker and length included.</returns>
    private static byte[] BuildApp1(int orientation)
    {
        // Offsets are relative to the start of the TIFF header. IFD0 sits at 8 and is 30 bytes
        // (a two-byte count, two twelve-byte entries, and a four-byte next-IFD pointer), which puts
        // the GPS IFD at 38.
        const int gpsIfdOffset = 38;

        var tiff = new List<byte>();

        tiff.AddRange("II"u8);                                   // little-endian
        tiff.AddRange([0x2A, 0x00]);                             // TIFF magic
        tiff.AddRange(BitConverter.GetBytes(8));                 // offset of IFD0

        tiff.AddRange(BitConverter.GetBytes((ushort)2));         // two entries
        tiff.AddRange(Entry(0x0112, type: 3, count: 1, value: (uint)orientation));
        tiff.AddRange(Entry(0x8825, type: 4, count: 1, value: gpsIfdOffset));
        tiff.AddRange(BitConverter.GetBytes(0));                 // no further IFD

        tiff.AddRange(BitConverter.GetBytes((ushort)1));         // one GPS entry
        tiff.AddRange(Entry(0x0001, type: 2, count: 2, value: 'N'));
        tiff.AddRange(BitConverter.GetBytes(0));

        var payload = new List<byte>("Exif"u8.ToArray()) { 0x00, 0x00 };

        payload.AddRange(tiff);

        // The length field counts itself but not the two marker bytes, and is big-endian — JPEG
        // segment framing is big-endian regardless of the TIFF block's own byte order.
        var length = payload.Count + 2;

        return [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
    }

    /// <summary>Builds one twelve-byte TIFF directory entry.</summary>
    /// <param name="tag">Tag number.</param>
    /// <param name="type">Field type — 2 is ASCII, 3 is SHORT, 4 is LONG.</param>
    /// <param name="count">Number of values.</param>
    /// <param name="value">The value, small enough to sit inline.</param>
    private static byte[] Entry(ushort tag, ushort type, uint count, uint value) =>
    [
        .. BitConverter.GetBytes(tag),
        .. BitConverter.GetBytes(type),
        .. BitConverter.GetBytes(count),
        .. BitConverter.GetBytes(value),
    ];
}
