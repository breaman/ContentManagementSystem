using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Core.Tests.Media;

/// <summary>
/// Rendition URL signing, key rotation, and request parsing (tasks P5-14, P5-15, P5-18, P5-29).
/// </summary>
/// <remarks>
/// The refusals are the substance here. An image endpoint that accepts arbitrary parameters is a
/// denial-of-service amplifier — a few hundred requests for distinct sizes pin every core and fill
/// the disk — so "a tampered URL is refused" is the property the whole design rests on (ADR 0007).
/// </remarks>
public class MediaUrlSignerTests
{
    private static readonly string Key = Convert.ToBase64String(new byte[32]);

    private static readonly string OtherKey = Convert.ToBase64String(Enumerable.Repeat((byte)7, 32).ToArray());

    private static readonly RenditionSpec Spec = new(
        812, 1280, 720, RenditionMode.Crop, ImageOutputFormat.Webp, RenditionSpec.DefaultQuality, 3);

    private static MediaUrlSigner Signer(MediaSigningOptions options, FakeTimeProvider? clock = null) =>
        new(options, clock ?? new FakeTimeProvider(), NullLogger<MediaUrlSigner>.Instance);

    [Test]
    public void ASignatureThisSignerProducedValidates()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        signer.Validate(Spec, signer.Sign(Spec)).Should().BeTrue();
    }

    [Test]
    public void AnUnsignedRequestIsRefused()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        signer.Validate(Spec, null).Should().BeFalse();
        signer.Validate(Spec, string.Empty).Should().BeFalse();
    }

    [Test]
    [Arguments(2560)]
    [Arguments(320)]
    public void ATamperedWidthIsRefused(int width)
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });
        var signature = signer.Sign(Spec);

        // The whole point: an attacker cannot walk the size space, because every size is a
        // different signature they cannot produce.
        signer.Validate(Spec with { Width = width }, signature).Should().BeFalse();
    }

    [Test]
    public void ATamperedQualityIsRefused()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        signer.Validate(Spec with { Quality = 95 }, signer.Sign(Spec)).Should().BeFalse();
    }

    [Test]
    public void ATamperedCropIsRefused()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        signer.Validate(
            Spec with { Crop = new NormalizedRect(0, 0, 0.5, 0.5) }, signer.Sign(Spec)).Should().BeFalse();
    }

    [Test]
    public void ASignatureFromAnotherSiteIsRefused()
    {
        var mine = Signer(new MediaSigningOptions { Key = Key });
        var theirs = Signer(new MediaSigningOptions { Key = OtherKey });

        mine.Validate(Spec, theirs.Sign(Spec)).Should().BeFalse();
    }

    [Test]
    public void AnEditBumpChangesTheSignature()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        // What makes a library edit invalidate every cached URL for the item without a purge.
        signer.Validate(Spec with { EditsVersion = 4 }, signer.Sign(Spec)).Should().BeFalse();
    }

    [Test]
    public void TheRetiredKeyStillValidatesDuringTheGracePeriod()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-16T10:00:00Z"));
        var old = Signer(new MediaSigningOptions { Key = OtherKey }, clock);

        var rotated = Signer(
            new MediaSigningOptions
            {
                Key = Key,
                PreviousKey = OtherKey,
                PreviousKeyExpiresOn = clock.GetUtcNow().AddDays(7),
            },
            clock);

        // Rendition URLs are embedded in every cached page and CDN copy. Without the grace period a
        // rotation breaks every image on the site at once (spec section 20.8).
        rotated.Validate(Spec, old.Sign(Spec)).Should().BeTrue();
    }

    [Test]
    public void TheRetiredKeyStopsValidatingWhenTheGracePeriodEnds()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-08-16T10:00:00Z"));
        var old = Signer(new MediaSigningOptions { Key = OtherKey }, clock);
        var signature = old.Sign(Spec);

        var rotated = Signer(
            new MediaSigningOptions
            {
                Key = Key,
                PreviousKey = OtherKey,
                PreviousKeyExpiresOn = clock.GetUtcNow().AddDays(7),
            },
            clock);

        clock.Advance(TimeSpan.FromDays(8));

        rotated.Validate(Spec, signature).Should().BeFalse();
    }

    [Test]
    public void AKeyTooShortToBeOneIsIgnoredRatherThanUsed()
    {
        // A four-byte "key" from a mistaken configuration must not become the site's signing secret.
        // The signer falls back to a generated one, which fails closed rather than open.
        var signer = Signer(new MediaSigningOptions { Key = Convert.ToBase64String(new byte[4]) });
        var other = Signer(new MediaSigningOptions { Key = Convert.ToBase64String(new byte[4]) });

        signer.Validate(Spec, other.Sign(Spec)).Should().BeFalse();
    }

    [Test]
    public void ABuiltUrlParsesBackToTheSpecItCameFrom()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });
        var spec = Spec with { FocalPoint = new NormalizedPoint(0.25, 0.75) };
        var url = signer.BuildUrl(spec, "Hero Banner.jpg");

        var uri = new Uri($"https://example.test{url}");
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        var parsed = RenditionRequestParser.TryParse(new RenditionRequest(
            int.Parse(segments[1]),
            segments[2],
            segments[3],
            segments[4],
            query["v"],
            query["q"],
            query["f"],
            query["c"]));

        // Building and parsing have to be exact inverses, or every URL the site emits is one the
        // site itself refuses.
        parsed.IsSuccess.Should().BeTrue();
        parsed.Spec.Should().Be(spec);
        signer.Validate(parsed.Spec!, query["s"]).Should().BeTrue();
    }

    [Test]
    public void AnAvifRequestIsRefusedAtTheParsingLayer()
    {
        var parsed = RenditionRequestParser.TryParse(
            new RenditionRequest(812, "1280x720", "crop", "hero.avif"));

        // Refused before generation, because the encoder returns null rather than throwing and the
        // response would otherwise be a cacheable 200 with an empty body (spec section 13.9.1).
        parsed.IsSuccess.Should().BeFalse();
        parsed.FailureCode.Should().Be(MediaCodes.AvifNotSupported);
    }

    [Test]
    [Arguments("1281x720", "crop", "hero.webp")]
    [Arguments("1280x720", "squish", "hero.webp")]
    [Arguments("1280x720", "crop", "hero.tiff")]
    [Arguments("1280x-720", "crop", "hero.webp")]
    [Arguments("x720", "crop", "hero.webp")]
    [Arguments("1280x720", "crop", "hero")]
    public void ARenditionThisSiteDoesNotServeIsRefused(string size, string mode, string name) =>
        RenditionRequestParser.TryParse(new RenditionRequest(812, size, mode, name))
            .IsSuccess.Should().BeFalse();

    [Test]
    public void MalformedGeometryIsRefused() =>
        RenditionRequestParser.TryParse(
                new RenditionRequest(812, "1280x720", "crop", "hero.webp", FocalPoint: "0.5"))
            .IsSuccess.Should().BeFalse();

    [Test]
    public void GeometryOutsideTheImageIsRefused() =>
        RenditionRequestParser.TryParse(
                new RenditionRequest(812, "1280x720", "crop", "hero.webp", FocalPoint: "1.5,0.5"))
            .IsSuccess.Should().BeFalse();

    [Test]
    public void AnOriginalSignatureCannotBeReplayedAsARendition()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        // Different handlers with different rules about what may be served; one signature must not
        // open both doors.
        signer.Validate(Spec, signer.SignOriginal(Spec.MediaItemId, Spec.EditsVersion)).Should().BeFalse();
        signer.ValidateOriginal(Spec.MediaItemId, Spec.EditsVersion, signer.Sign(Spec)).Should().BeFalse();
    }

    [Test]
    public void AnOriginalSignatureIsBoundToTheEditsVersion()
    {
        var signer = Signer(new MediaSigningOptions { Key = Key });

        signer.ValidateOriginal(812, 4, signer.SignOriginal(812, 3)).Should().BeFalse();
        signer.ValidateOriginal(812, 3, signer.SignOriginal(812, 3)).Should().BeTrue();
    }
}
