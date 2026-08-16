using System.Text.RegularExpressions;

using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The responsive <c>&lt;picture&gt;</c> the <c>media</c> renderer emits (task P5-20,
/// spec section 13.6, acceptance criteria P5 #7 and P5 #10).
/// </summary>
/// <remarks>
/// Asserted against the markup rather than against <c>ResponsiveImages</c> alone, because half of
/// what spec section 13.6 asks for is about attributes rather than arithmetic — which image is
/// eager, which is lazy, and whether the dimensions reached the element at all. The other half, the
/// candidate widths, is arithmetic and is asserted here too because the two are only correct
/// together: a perfect <c>srcset</c> on an <c>&lt;img&gt;</c> with no <c>width</c> still shifts the
/// layout.
/// <para>
/// The signer under these is the real one, so every URL asserted on is one the delivery endpoint
/// would accept — a <c>srcset</c> full of unsigned URLs would render identically here and 403 on
/// every request in production.
/// </para>
/// </remarks>
public class MediaPictureRendererTests : IDisposable
{
    private readonly FieldRendererHarness _harness = new();

    public MediaPictureRendererTests() =>
        _harness.Media.Add(812, width: 2000, height: 1500, altText: "A quiet street");

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <remarks>Acceptance criterion P5 #10.</remarks>
    [Fact]
    public void APlacedImageIsAPictureWithAWebpSourceAnAccurateSrcsetAndExplicitDimensions()
    {
        var markup = _harness.Render("""{"type":"media","mediaId":812}""");

        markup.Should().Contain("<picture").And.Contain("<source type=\"image/webp\"");
        markup.Should().Contain("alt=\"A quiet street\"");

        // The largest candidate a 2000 px original can offer without being enlarged: 1920 is on the
        // allowlist, and 2560 resolves back down to the source's own 2000.
        markup.Should().Contain("width=\"2000\"").And.Contain("height=\"1500\"");

        // Every descriptor is the width the browser will actually receive. 2560 appears in the URL
        // because it is what was signed, and 2000w beside it because that is what comes back.
        Descriptors(markup).Should().Equal(320, 640, 960, 1280, 1920, 2000);
    }

    /// <remarks>Acceptance criterion P5 #10 — the AVIF half.</remarks>
    [Fact]
    public void NoSourceEverOffersAvifBecauseNothingCouldProduceIt()
    {
        var markup = _harness.Render("""{"type":"media","mediaId":812}""");

        // Advertising a format the endpoint refuses at the spec-parsing layer would leave a browser
        // that prefers it with no picture at all (spec section 13.9.1).
        markup.Should().NotContain("avif");
    }

    [Fact]
    public void EveryCandidateUrlIsSignedByThisDeployment()
    {
        var markup = _harness.Render("""{"type":"media","mediaId":812}""");

        var urls = Regex.Matches(markup, @"/media/812/[^\s""]+").Select(match => match.Value).ToList();

        urls.Should().NotBeEmpty();
        urls.Should().OnlyContain(url => url.Contains("&amp;s=", StringComparison.Ordinal));
    }

    [Fact]
    public void AnImageIsNeverEnlargedPastTheOriginalItWasUploadedAt()
    {
        _harness.Media.Add(500, width: 400, height: 300);

        var markup = _harness.Render("""{"type":"media","mediaId":500}""");

        // 640 is signed and asked for; 400 is what comes back, and 400 is what the descriptor and
        // the width attribute say. Upscaling would produce a blurrier file several times larger.
        Descriptors(markup).Should().Equal(320, 400);
        markup.Should().Contain("width=\"400\"").And.Contain("height=\"300\"");
    }

    /// <remarks>
    /// Spec section 13.6 asks for eager loading and high fetch priority on the first image in the
    /// first zone, and lazy loading everywhere else.
    /// </remarks>
    [Fact]
    public void TheFirstImageOnThePageIsEagerAndHighPriorityAndEveryLaterOneIsLazy()
    {
        _harness.Media.Add(813, width: 1200, height: 800);

        var context = RenderingHarness.Context(RenderingHarness.Payload(
            ("hero", """{"type":"media","mediaId":812}"""),
            ("body", """{"type":"media","mediaId":813}""")));

        var hero = _harness.RenderIn(context, "hero");
        var body = _harness.RenderIn(context, "body");

        hero.Should().Contain("loading=\"eager\"").And.Contain("fetchpriority=\"high\"");

        // Deliberately absent on the LCP image: it tells the browser it may paint the rest of the
        // page first, which is the wrong instruction for the element the metric watches.
        hero.Should().NotContain("decoding=");

        body.Should().Contain("loading=\"lazy\"").And.Contain("decoding=\"async\"");
        body.Should().NotContain("fetchpriority");
    }

    [Fact]
    public void ALibraryRotationChangesTheDimensionsTheMarkupReserves()
    {
        // The whole reason the renderer cannot emit the row's own width and height: a quarter turn
        // makes a 2000×1500 photograph 1500×2000, and markup that said otherwise would reserve a
        // box the picture never fills.
        _harness.Media.Add(814, width: 2000, height: 1500, edits: new MediaEdits(Rotate: 90));

        var markup = _harness.Render("""{"type":"media","mediaId":814}""");

        markup.Should().Contain("width=\"1500\"").And.Contain("height=\"2000\"");
    }

    /// <remarks>Acceptance criterion P5 #7, at the renderer.</remarks>
    [Fact]
    public void AUsageCropChangesOnlyTheUrlsOfThePlacementThatCarriesIt()
    {
        var context = RenderingHarness.Context(RenderingHarness.Payload(
            ("hero", """{"type":"media","mediaId":812,"crop":{"x":0,"y":0.25,"w":1,"h":0.5}}"""),
            ("body", """{"type":"media","mediaId":812}""")));

        var cropped = _harness.RenderIn(context, "hero");
        var whole = _harness.RenderIn(context, "body");

        // Half the height of the source, so the same widths resolve to half the heights — and the
        // signature covers the crop, so not one URL is shared between the two placements.
        cropped.Should().Contain("width=\"2000\"").And.Contain("height=\"750\"");
        whole.Should().Contain("width=\"2000\"").And.Contain("height=\"1500\"");

        Urls(cropped).Should().NotIntersectWith(Urls(whole));
    }

    [Fact]
    public void TheSizesAttributeComesFromTheSlotConfigurationAndDefaultsToTheFullViewport()
    {
        _harness.Render("""{"type":"media","mediaId":812}""")
            .Should().Contain("sizes=\"100vw\"");

        _harness.Render(
                """{"type":"media","mediaId":812}""",
                FieldRendererHarness.Schema(FieldTypeKeys.Media, """{"sizes":"(max-width: 768px) 100vw, 800px"}"""))
            .Should().Contain("sizes=\"(max-width: 768px) 100vw, 800px\"");
    }

    [Fact]
    public void ADecorativeImageRendersAnEmptyAltAndAPlacementMayOverrideTheLibrarysWords()
    {
        _harness.Media.Add(815, width: 800, height: 600, altText: null, isDecorative: true);

        _harness.Render("""{"type":"media","mediaId":815}""").Should().Contain("alt=\"\"");

        _harness.Render("""{"type":"media","mediaId":812,"altOverride":"The same street at dusk"}""")
            .Should().Contain("alt=\"The same street at dusk\"");
    }

    [Fact]
    public void AnUndescribedImageRendersAnEmptyAltAndSaysSoInTheLog()
    {
        // Publishing this fails validation (task P5-21), so reaching it means the content predates
        // the rule. An empty alt plus a log entry is the honest answer; inventing a description
        // would be worse than saying nothing.
        _harness.Media.Add(816, width: 800, height: 600, altText: null);

        _harness.Render("""{"type":"media","mediaId":816}""").Should().Contain("alt=\"\"");

        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("816"));
    }

    [Fact]
    public void AFormatWithNoRenditionPipelineIsShownAtItsSignedOriginal()
    {
        // SkiaSharp has no SVG rasterizer, and an animated GIF through a still-image encoder comes
        // back as one frame. Neither has a rendition, so neither gets a <picture>.
        _harness.Media.Add(817, contentType: "image/svg+xml", altText: "A logo");

        var markup = _harness.Render("""{"type":"media","mediaId":817}""");

        markup.Should().NotContain("<picture").And.Contain("/media/817/file/");
        markup.Should().Contain("alt=\"A logo\"");
    }

    [Fact]
    public void ADocumentIsALinkRatherThanABrokenImage()
    {
        _harness.Media.Add(
            818,
            width: null,
            height: null,
            altText: null,
            contentType: "application/pdf",
            kind: MediaKind.Document);

        var markup = _harness.Render("""{"type":"media","mediaId":818}""");

        markup.Should().Contain("<a").And.Contain("cms-media-file").And.Contain("/media/818/file/");
        markup.Should().NotContain("<img");
    }

    [Fact]
    public void AGalleryRendersEveryItemThroughTheSinglePictureRenderer()
    {
        _harness.Media.Add(813, width: 1200, height: 800);

        var markup = _harness.Render(
            """{"type":"mediaList","items":[{"mediaId":812},{"mediaId":813}]}""");

        markup.Should().Contain("<ul class=\"cms-media-list\">");
        Regex.Matches(markup, "<picture").Should().HaveCount(2);

        // Only the first image in the gallery is the page's likely LCP element; the second is not,
        // even though both are in the same zone.
        Regex.Matches(markup, "loading=\"eager\"").Should().HaveCount(1);
        Regex.Matches(markup, "loading=\"lazy\"").Should().HaveCount(1);
    }

    /// <summary>The <c>w</c> descriptors of the first srcset in the markup, in order.</summary>
    private static IReadOnlyList<int> Descriptors(string markup) =>
        [.. Regex.Matches(markup, @"\s(\d+)w[,""]").Select(match => int.Parse(match.Groups[1].Value))
            .Distinct()];

    /// <summary>Every rendition URL in the markup.</summary>
    private static IReadOnlyList<string> Urls(string markup) =>
        [.. Regex.Matches(markup, @"/media/\d+/\d+x\d+/[^\s""]+").Select(match => match.Value)];
}
