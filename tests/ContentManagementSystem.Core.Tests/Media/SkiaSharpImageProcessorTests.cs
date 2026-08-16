using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Media.Upload;

using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

using Microsoft.Extensions.Logging.Abstractions;

using SkiaSharp;

namespace ContentManagementSystem.Core.Tests.Media;

/// <summary>
/// The image processor (tasks P5-07 and P5-09, spec sections 13.3 and 13.9).
/// </summary>
/// <remarks>
/// These decode and encode real images through the deployed native library, which is the only way
/// the two properties that matter can be asserted: that the declared formats can actually be
/// produced, and that metadata does not survive the pipeline.
/// </remarks>
public class SkiaSharpImageProcessorTests
{
    private readonly SkiaSharpImageProcessor _processor = new(NullLogger<SkiaSharpImageProcessor>.Instance);

    [Fact]
    public void TheDeclaredFormatsCanActuallyBeEncoded()
    {
        // The startup assertion of task P5-09. Skia answers an unsupported encode with null rather
        // than an exception, so without this a native build missing an encoder would serve empty
        // images and log nothing (spec section 13.9.1).
        var act = () => _processor.AssertCapabilities();

        act.Should().NotThrow();
    }

    [Fact]
    public void AvifIsNotOffered() =>
        // Not a capability this implementation has; declaring it would mean renditions that fail
        // silently (ADR 0011).
        _processor.SupportedOutputFormats.Should().BeEquivalentTo(
            [ImageOutputFormat.Jpeg, ImageOutputFormat.Png, ImageOutputFormat.Webp]);

    [Fact]
    public void ProbeReportsDimensionsWithoutDecoding()
    {
        using var content = new MemoryStream(TestImages.Encode(800, 600));

        var probe = _processor.Probe(content);

        probe.Should().NotBeNull();
        probe!.Width.Should().Be(800);
        probe.Height.Should().Be(600);
        probe.Format.Should().Be(MediaByteFormat.Jpeg);
        probe.PixelCount.Should().Be(480_000);
    }

    [Fact]
    public void ProbeReadsTheExifOrientation()
    {
        using var content = new MemoryStream(TestImages.EncodeWithExif(800, 600, orientation: 6));

        var probe = _processor.Probe(content);

        probe!.Rotation.Should().Be(90);
        probe.Mirrored.Should().BeFalse();

        // A quarter turn swaps the reported dimensions, which is why orientation is baked in rather
        // than carried as a flag every later calculation would have to remember.
        probe.OrientedSize.Should().Be(new PixelSize(600, 800));
    }

    [Fact]
    public void ProbeReturnsNothingForSomethingThatIsNotAnImage()
    {
        using var content = new MemoryStream("<html><body>not an image</body></html>"u8.ToArray());

        _processor.Probe(content).Should().BeNull();
    }

    [Fact]
    public void NormalizingBakesTheOrientationIntoThePixels()
    {
        using var content = new MemoryStream(TestImages.EncodeWithExif(800, 600, orientation: 6));

        var probe = _processor.Probe(content)!;
        var normalized = _processor.NormalizeOriginal(content, probe);

        normalized.Should().NotBeNull();
        normalized!.Width.Should().Be(600);
        normalized.Height.Should().Be(800);
    }

    [Fact]
    public void NormalizingStripsEveryMetadataBlock()
    {
        var source = TestImages.EncodeWithExif(800, 600, orientation: 6);

        // The fixture genuinely carries what has to be removed — otherwise this test would pass
        // against an image that never had GPS in it.
        ReadDirectories(source).OfType<GpsDirectory>().Should().NotBeEmpty();

        using var content = new MemoryStream(source);

        var probe = _processor.Probe(content)!;
        var normalized = _processor.NormalizeOriginal(content, probe)!;

        // GPS coordinates in a published photograph are a privacy incident, and the stored original
        // is what a "download original" action serves (spec section 13.3 step 8).
        ReadDirectories(normalized.Bytes).OfType<GpsDirectory>().Should().BeEmpty();
        ReadDirectories(normalized.Bytes).OfType<ExifIfd0Directory>().Should().BeEmpty();
    }

    [Fact]
    public void NormalizingAnUprightImageStillReEncodesIt()
    {
        var source = TestImages.EncodeWithExif(400, 300, orientation: 1);

        using var content = new MemoryStream(source);

        var probe = _processor.Probe(content)!;
        var normalized = _processor.NormalizeOriginal(content, probe)!;

        // "Only re-encode when a rotation is needed" would leave the metadata on exactly those
        // photographs that happened to be taken level.
        ReadDirectories(normalized.Bytes).OfType<GpsDirectory>().Should().BeEmpty();
        normalized.Width.Should().Be(400);
    }

    [Fact]
    public void RenderingProducesTheRequestedBoxInTheRequestedFormat()
    {
        using var content = new MemoryStream(TestImages.Encode(4000, 3000));

        var spec = new RenditionSpec(
            1, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, RenditionSpec.DefaultQuality, 0);

        var rendered = _processor.Render(content, spec, MediaEdits.None);

        rendered.Should().NotBeNull();
        rendered!.Width.Should().Be(1280);
        rendered.Height.Should().Be(720);
        rendered.MimeType.Should().Be("image/webp");

        // Decoded back, because "produced some bytes" and "produced a WebP" are different claims.
        using var decoded = SKBitmap.Decode(rendered.Bytes);

        decoded.Width.Should().Be(1280);
    }

    [Fact]
    public void RenderingAppliesTheLibraryRotationBeforeTheUsageCrop()
    {
        using var content = new MemoryStream(TestImages.Encode(4000, 2000));

        var spec = new RenditionSpec(
            1, 640, 640, RenditionMode.Contain, ImageOutputFormat.Jpeg, RenditionSpec.DefaultQuality, 1);

        var rendered = _processor.Render(content, spec, new MediaEdits(Rotate: 90))!;

        // The quarter turn makes a 4000×2000 source 2000×4000, so a 640 box fits its height.
        rendered.Height.Should().Be(640);
        rendered.Width.Should().Be(320);
    }

    [Fact]
    public void ARenditionCarriesNoMetadataEither()
    {
        using var content = new MemoryStream(TestImages.EncodeWithExif(2000, 1500, orientation: 1));

        var spec = new RenditionSpec(
            1, 640, 480, RenditionMode.Crop, ImageOutputFormat.Jpeg, RenditionSpec.DefaultQuality, 0);

        var rendered = _processor.Render(content, spec, MediaEdits.None)!;

        ReadDirectories(rendered.Bytes).OfType<GpsDirectory>().Should().BeEmpty();
    }

    [Fact]
    public void PaddingReturnsTheRequestedBoxExactly()
    {
        using var content = new MemoryStream(TestImages.Encode(2000, 500));

        var spec = new RenditionSpec(
            1, 640, 640, RenditionMode.Pad, ImageOutputFormat.Png, RenditionSpec.DefaultQuality, 0);

        var rendered = _processor.Render(content, spec, MediaEdits.None)!;

        rendered.Width.Should().Be(640);
        rendered.Height.Should().Be(640);
    }

    private static IReadOnlyList<MetadataExtractor.Directory> ReadDirectories(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);

        return ImageMetadataReader.ReadMetadata(stream);
    }
}
