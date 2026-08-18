using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// Slug generation and the rules a URL segment must satisfy (task P2-07, spec sections 10.2 and 10.3).
/// </summary>
/// <remarks>
/// The pairing is the point: a generated slug and a hand-typed one land in the same column, so every
/// case here that generates one also asserts that the result passes validation. A generator whose
/// output its own validator rejects is a bug that only shows up for titles nobody tried.
/// </remarks>
public class SlugsTests
{
    [Test]
    [Arguments("Pricing", "pricing")]
    [Arguments("Our Team", "our-team")]
    [Arguments("  Leading and trailing  ", "leading-and-trailing")]
    [Arguments("What's New?", "what-s-new")]
    [Arguments("C# for Beginners", "c-for-beginners")]
    [Arguments("2026 Annual Report", "2026-annual-report")]
    [Arguments("Slashes/and\\backslashes", "slashes-and-backslashes")]
    [Arguments("Multiple   spaces --- and dashes", "multiple-spaces-and-dashes")]
    public void ASlugIsDerivedFromTheTitle(string title, string expected)
    {
        var slug = Slugs.Generate(title);

        slug.Should().Be(expected);
        Slugs.Validate(slug, isRootLevel: true).IsValid.Should().BeTrue();
    }

    [Test]
    public void AccentsAreFoldedToTheirBaseLetters()
    {
        // The "normalized to ASCII where unambiguous" half of spec section 10.2. A café that is
        // reachable at /cafe is the outcome an editor expects and the one a link they typed by hand
        // will match.
        var slug = Slugs.Generate("Café Crème — Réservations");

        slug.Should().Be("cafe-creme-reservations");
        Slugs.Validate(slug, isRootLevel: true).IsValid.Should().BeTrue();
    }

    [Test]
    public void ALetterWithNoAsciiFormIsKeptRatherThanDropped()
    {
        // Dropping it would turn a title in a non-Latin script into an empty slug and leave the
        // editor with an error they cannot act on. Spec section 10.3 permits the Unicode slug and
        // asks for a warning instead, which the next test asserts.
        var slug = Slugs.Generate("Привет Мир");

        slug.Should().Be("привет-мир");
    }

    [Test]
    public void ANonAsciiSlugIsAcceptedWithAHomographWarning()
    {
        var result = Slugs.Validate("привет-мир", isRootLevel: true);

        result.HasErrors.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Match<ValidationDiagnostic>(d =>
                d.Code == PageCodes.SlugHomograph && d.Severity == ValidationSeverity.Warning);
    }

    [Test]
    public void ATitleWithNothingUsableInItProducesNoSlug()
    {
        // Not an exception and not a made-up slug: the editor is told to type one, which is the only
        // honest answer for a title of "!!!".
        Slugs.Generate("!!! ??? ---").Should().BeEmpty();

        Slugs.Validate(string.Empty, isRootLevel: true).Diagnostics
            .Should().ContainSingle().Which.Code.Should().Be(PageCodes.SlugRequired);
    }

    [Test]
    public void AGeneratedSlugIsCutToTheColumnAndNeverEndsOnAHyphen()
    {
        var slug = Slugs.Generate(string.Join(' ', Enumerable.Repeat("verylongword", 40)));

        slug.Length.Should().BeLessThanOrEqualTo(FieldLengths.Slug);
        slug.Should().NotEndWith("-");
        Slugs.Validate(slug, isRootLevel: true).IsValid.Should().BeTrue();
    }

    [Test]
    [Arguments("-leading")]
    [Arguments("trailing-")]
    [Arguments("double--hyphen")]
    [Arguments("has space")]
    [Arguments("has/slash")]
    [Arguments("Uppercase")]
    [Arguments("has.dot")]
    [Arguments("has_underscore")]
    public void AnUnusableSegmentIsRefused(string slug) =>
        Slugs.Validate(slug, isRootLevel: true).Diagnostics
            .Should().ContainSingle().Which.Code.Should().Be(PageCodes.SlugFormat);

    [Test]
    public void ASegmentLongerThanTheColumnIsRefused() =>
        Slugs.Validate(new string('a', FieldLengths.Slug + 1), isRootLevel: true).Diagnostics
            .Should().ContainSingle().Which.Code.Should().Be(PageCodes.TooLong);

    [Test]
    [Arguments("admin")]
    [Arguments("api")]
    [Arguments("media")]
    [Arguments("preview")]
    [Arguments("health")]
    public void AReservedFirstSegmentIsRefusedAtTheRootAndAllowedBeneathAParent(string slug)
    {
        Slugs.Validate(slug, isRootLevel: true).Diagnostics
            .Should().ContainSingle().Which.Code.Should().Be(PageCodes.SlugReserved);

        // The reserved list is a set of *first* segments, and /products/admin reaches no framework
        // endpoint. Refusing it everywhere would forbid a perfectly ordinary page name.
        Slugs.Validate(slug, isRootLevel: false).IsValid.Should().BeTrue();
    }

    [Test]
    public void CaseAndNormalizationFormAreFoldedRatherThanRefused()
    {
        // Neither is a mistake an editor made. URLs are lowercase by configuration, and two byte
        // sequences that render identically must not be able to occupy two rows.
        Slugs.Normalize("  Our-Team  ").Should().Be("our-team");

        // "cafe" plus a combining acute against the precomposed form: different bytes, the
        // same word, and two rows would be the wrong number of rows for one URL.
        Slugs.Normalize("cafe\u0301").Should().Be("caf\u00e9");
    }
}
