using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Tests.Media;

/// <summary>
/// Focal-point crop arithmetic and rendition spec normalization (task P5-26,
/// spec sections 13.4 and 13.5).
/// </summary>
/// <remarks>
/// Exhaustive here rather than through rendered images, because this is where the off-by-one errors
/// live and an image comparison would report them as "the picture looks slightly wrong". None of
/// these tests decodes anything, so none of them needs a native library.
/// </remarks>
public class RenditionGeometryTests
{
    [Fact]
    public void AWideSourceCroppedToASquareKeepsFullHeight()
    {
        var crop = RenditionGeometry.FocalCrop(new PixelSize(4000, 2000), new PixelSize(600, 600), null);

        crop.Height.Should().Be(2000);
        crop.Width.Should().Be(2000);
        crop.Y.Should().Be(0);
        crop.X.Should().Be(1000, "a centred crop leaves equal margins");
    }

    [Fact]
    public void ATallSourceCroppedToAWideBoxKeepsFullWidth()
    {
        var crop = RenditionGeometry.FocalCrop(new PixelSize(1000, 4000), new PixelSize(1200, 600), null);

        crop.Width.Should().Be(1000);
        crop.Height.Should().Be(500);
        crop.X.Should().Be(0);
        crop.Y.Should().Be(1750);
    }

    [Fact]
    public void TheFocalPointMovesTheCrop()
    {
        var crop = RenditionGeometry.FocalCrop(
            new PixelSize(4000, 2000), new PixelSize(600, 600), new NormalizedPoint(0.25, 0.5));

        // The subject sits a quarter of the way across, so the 2000px window centres on x=1000.
        crop.X.Should().Be(0);
        crop.Width.Should().Be(2000);
    }

    [Fact]
    public void AFocalPointNearAnEdgeSlidesTheCropRatherThanShrinkingIt()
    {
        var crop = RenditionGeometry.FocalCrop(
            new PixelSize(4000, 2000), new PixelSize(600, 600), new NormalizedPoint(0.99, 0.5));

        // Full-bleed either way: the window is the same size and simply stops at the edge. Shrinking
        // it instead would produce a smaller, softer rendition for images whose subject is off to
        // one side.
        crop.Width.Should().Be(2000);
        crop.X.Should().Be(2000);
        (crop.X + crop.Width).Should().Be(4000);
    }

    [Fact]
    public void ACropNeverExtendsPastTheSource()
    {
        var crop = RenditionGeometry.ToPixels(new PixelSize(1000, 800), new NormalizedRect(0.9, 0.9, 0.2, 0.2));

        (crop.X + crop.Width).Should().BeLessThanOrEqualTo(1000);
        (crop.Y + crop.Height).Should().BeLessThanOrEqualTo(800);
    }

    [Fact]
    public void AFullCropIsTheWholeImage() =>
        RenditionGeometry.ToPixels(new PixelSize(1280, 720), NormalizedRect.Full)
            .Should().Be(new PixelRect(0, 0, 1280, 720));

    [Fact]
    public void CropModeReturnsTheRequestedBoxExactly() =>
        // What makes the width and height safe to write into the <img> tag, which is what protects
        // Cumulative Layout Shift (spec section 13.6).
        RenditionGeometry.Resolve(new PixelSize(4000, 3000), new PixelSize(1280, 720), RenditionMode.Crop)
            .Should().Be(new PixelSize(1280, 720));

    [Fact]
    public void ContainModeFitsInsideTheBox() =>
        RenditionGeometry.Resolve(new PixelSize(4000, 2000), new PixelSize(1280, 1280), RenditionMode.Contain)
            .Should().Be(new PixelSize(1280, 640));

    [Fact]
    public void NothingIsEverUpscaled()
    {
        // A 400px original asked for at 1280px comes back at 400: upscaling produces a blurrier file
        // that is several times larger, silently.
        RenditionGeometry.Resolve(new PixelSize(400, 300), new PixelSize(1280, 960), RenditionMode.Contain)
            .Should().Be(new PixelSize(400, 300));

        RenditionGeometry.Resolve(new PixelSize(400, 300), new PixelSize(1280, 960), RenditionMode.Crop)
            .Should().Be(new PixelSize(400, 300));
    }

    [Fact]
    public void ASmallSourceKeepsTheRequestedAspectRatio()
    {
        var size = RenditionGeometry.Resolve(new PixelSize(600, 600), new PixelSize(1280, 720), RenditionMode.Crop);

        ((double)size.Width / size.Height).Should().BeApproximately(1280d / 720, 0.01);
    }

    [Fact]
    public void TheCanonicalSpecIsStableAcrossEquivalentSpellings()
    {
        var first = new RenditionSpec(
            812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 82, 3,
            new NormalizedPoint(1.0 / 3, 0.5));

        var second = new RenditionSpec(
            812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 82, 3,
            new NormalizedPoint(0.33333333333, 0.5));

        // Two spellings of one picture must hash to one row and cost one encode.
        first.ToCanonicalString().Should().Be(second.ToCanonicalString());
        first.ComputeHash().Should().Equal(second.ComputeHash());
    }

    [Fact]
    public void TheEditsVersionIsPartOfTheIdentity()
    {
        var before = new RenditionSpec(812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 82, 3);
        var after = before with { EditsVersion = 4 };

        // This is what makes a library edit bust every client and CDN cache without a purge
        // (ADR 0007).
        after.ToCanonicalString().Should().NotBe(before.ToCanonicalString());
    }

    [Theory]
    [InlineData(1281)]
    [InlineData(0)]
    [InlineData(-320)]
    public void AWidthOutsideTheAllowlistIsRefused(int width) =>
        new RenditionSpec(812, width, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 82, 0)
            .IsAllowed.Should().BeFalse();

    [Fact]
    public void AnAllowlistedWidthIsAccepted() =>
        new RenditionSpec(812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 82, 0)
            .IsAllowed.Should().BeTrue();

    [Fact]
    public void AQualityOutsideTheBoundsIsRefused() =>
        new RenditionSpec(812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 100, 0)
            .IsAllowed.Should().BeFalse();

    [Fact]
    public void AnImpossibleCropIsRefused() =>
        new RenditionSpec(
            812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, 82, 0,
            Crop: new NormalizedRect(0.8, 0, 0.5, 1)).IsAllowed.Should().BeFalse();
}
