using ContentManagementSystem.Core.Security;

using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// The three profiles and the rules that hold across them (task P1-18, spec section 20.2).
/// </summary>
/// <remarks>
/// <see cref="XssCorpusTests"/> asserts that nothing hostile survives. This file asserts the other
/// half: that the right benign things <em>do</em> survive, profile by profile. Over-stripping is
/// risk R3 and it has no attacker to catch it — the symptom is an author whose table disappeared,
/// reported weeks later as "the CMS ate my content".
/// </remarks>
public class SanitizationServiceTests
{
    private readonly SanitizationService _sanitizer = new();

    [Fact]
    public void BasicKeepsProseAndLinks()
    {
        const string Markup =
            "<p>Ship <strong>faster</strong> with <em>less</em> <code>toil</code>.</p>" +
            "<h2>Why</h2><ul><li>One</li></ul><blockquote>Quoted</blockquote>" +
            "<a href=\"https://example.test/x\" title=\"x\">link</a>";

        var sanitized = _sanitizer.Sanitize(Markup, SanitizationProfile.Basic);

        SanitizationAssertions.TagNames(sanitized).Should().Equal(
            "p", "strong", "em", "code", "h2", "ul", "li", "blockquote", "a");
    }

    [Theory]
    [InlineData("mailto:hello@example.test")]
    [InlineData("tel:+15555550123")]
    [InlineData("https://example.test/page")]
    [InlineData("/about/team")]
    [InlineData("#section")]
    public void TheSchemeAllowlistKeepsTheSchemesAuthorsActuallyUse(string href)
    {
        var sanitized = _sanitizer.Sanitize($"<a href=\"{href}\">x</a>", SanitizationProfile.Basic);

        sanitized.Should().Contain(href);
    }

    [Fact]
    public void AnUnknownWrapperIsUnwrappedRatherThanDeleted()
    {
        var sanitized = _sanitizer.Sanitize(
            "<section><p>Kept</p></section>",
            SanitizationProfile.Basic);

        // The tag is not on the Basic allowlist, but the author's paragraph is not the tag's fault.
        // Deleting the subtree here is how a sanitizer eats a pasted document (risk R3).
        sanitized.Should().Be("<p>Kept</p>");
    }

    [Fact]
    public void ACodeBearingElementIsDeletedWithItsContents()
    {
        var sanitized = _sanitizer.Sanitize(
            "<p>Before</p><script>alert('XSS')</script><p>After</p>",
            SanitizationProfile.Basic);

        // The mirror image of the case above, and the reason unwrapping cannot be the only rule:
        // unwrapping a script leaves its body behind as visible text.
        sanitized.Should().Be("<p>Before</p><p>After</p>");
    }

    [Fact]
    public void BasicRefusesTablesAndImagesThatExtendedAllows()
    {
        const string Markup = "<table><tr><td>Cell</td></tr></table><img src=\"https://cdn.test/a.png\" alt=\"a\">";

        SanitizationAssertions.TagNames(_sanitizer.Sanitize(Markup, SanitizationProfile.Basic))
            .Should().BeEmpty();

        SanitizationAssertions.TagNames(_sanitizer.Sanitize(Markup, SanitizationProfile.Extended))
            .Should().Contain(["table", "tr", "td", "img"]);
    }

    [Fact]
    public void ExtendedRefusesTheEmbedsDeveloperAllows()
    {
        const string Markup = "<iframe src=\"https://www.youtube.com/embed/x\"></iframe>";

        SanitizationAssertions.TagNames(_sanitizer.Sanitize(Markup, SanitizationProfile.Extended))
            .Should().BeEmpty();

        SanitizationAssertions.TagNames(_sanitizer.Sanitize(Markup, SanitizationProfile.Developer))
            .Should().Equal("iframe");
    }

    [Fact]
    public void AnIframePointingAtAnUnlistedHostIsRemovedEntirely()
    {
        var sanitized = _sanitizer.Sanitize(
            "<iframe src=\"https://www.youtube.com.evil.test/embed/x\"></iframe>",
            SanitizationProfile.Developer);

        // Not merely stripped of its src. An iframe with no src frames the embedding origin in some
        // browsers and an empty box in the rest, and neither is what the author asked for. The host
        // is also matched in full, so a suffix that ends in an allowlisted name does not pass.
        sanitized.Should().BeEmpty();
    }

    [Fact]
    public void AnIframeOverPlainHttpIsRefused()
    {
        var sanitized = _sanitizer.Sanitize(
            "<iframe src=\"http://www.youtube.com/embed/x\"></iframe>",
            SanitizationProfile.Developer);

        sanitized.Should().BeEmpty();
    }

    [Fact]
    public void DataAttributesAreDeveloperOnly()
    {
        const string Markup = "<div data-widget=\"pricing\">x</div>";

        _sanitizer.Sanitize(Markup, SanitizationProfile.Extended).Should().NotContain("data-widget");
        _sanitizer.Sanitize(Markup, SanitizationProfile.Developer).Should().Contain("data-widget");
    }

    [Fact]
    public void AnInlineImageSurvivesWhileAnInlineDocumentDoesNot()
    {
        // A one-pixel transparent GIF: an image, base64, and well under the cap.
        const string Pixel = "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

        _sanitizer.Sanitize($"<img src=\"{Pixel}\" alt=\"a\">", SanitizationProfile.Extended)
            .Should().Contain(Pixel);

        // Same scheme, wrong element and wrong media type. The scheme allowlist alone cannot tell
        // these apart, which is why the check is not expressed as one.
        _sanitizer.Sanitize($"<a href=\"{Pixel}\">x</a>", SanitizationProfile.Extended)
            .Should().NotContain("data:");
    }

    [Fact]
    public void AnInlineSvgImageIsRefusedEvenThoughItIsAnImage()
    {
        const string Svg = "data:image/svg+xml;base64,PHN2ZyBvbmxvYWQ9YWxlcnQoMSkvPg==";

        // An SVG is a document that can carry script. It arrives here as an opaque string, and
        // cleaning it would mean parsing a second document inside this one.
        _sanitizer.Sanitize($"<img src=\"{Svg}\" alt=\"a\">", SanitizationProfile.Developer)
            .Should().NotContain("data:");
    }

    [Fact]
    public void AnInlineImageOverTheCapIsRefused()
    {
        var sanitizer = new SanitizationService(new SanitizationOptions { MaxDataUriBytes = 64 });
        var oversized = "data:image/png;base64," + new string('A', 400);

        sanitizer.Sanitize($"<img src=\"{oversized}\" alt=\"a\">", SanitizationProfile.Extended)
            .Should().NotContain("data:");
    }

    [Fact]
    public void ANonBase64DataUriIsRefusedRatherThanMeasured()
    {
        // Percent-encoded rather than base64 is the shape a text/html payload takes when it is
        // trying not to look like one, so the encoding is required rather than inferred.
        _sanitizer.Sanitize(
            "<img src=\"data:image/png,%3Cscript%3Ealert(1)%3C/script%3E\" alt=\"a\">",
            SanitizationProfile.Extended)
            .Should().NotContain("data:");
    }

    [Fact]
    public void ATargetedLinkGetsNoopenerAndNoreferrer()
    {
        var sanitized = _sanitizer.Sanitize(
            "<a href=\"https://example.test\" target=\"_blank\">x</a>",
            SanitizationProfile.Basic);

        sanitized.Should().Contain("rel=\"noopener noreferrer\"");
    }

    [Fact]
    public void ForcingRelKeepsTheTokensTheAuthorWrote()
    {
        var sanitized = _sanitizer.Sanitize(
            "<a href=\"https://example.test\" target=\"_blank\" rel=\"nofollow\">x</a>",
            SanitizationProfile.Basic);

        // nofollow is an SEO decision. Overwriting rel wholesale would quietly reverse it.
        sanitized.Should().Contain("nofollow").And.Contain("noopener").And.Contain("noreferrer");
    }

    [Fact]
    public void ALinkThatOpensInPlaceIsLeftAlone()
    {
        var sanitized = _sanitizer.Sanitize(
            "<a href=\"https://example.test\" target=\"_self\">x</a>",
            SanitizationProfile.Basic);

        sanitized.Should().NotContain("rel=");
    }

    [Fact]
    public void AnInlineStyleKeepsAllowlistedPropertiesAndDropsTheRest()
    {
        var sanitized = _sanitizer.Sanitize(
            "<div style=\"text-align: center; position: fixed; z-index: 9999\">x</div>",
            SanitizationProfile.Extended);

        sanitized.Should().Contain("text-align");
        sanitized.Should().NotContain("position").And.NotContain("z-index");
    }

    [Fact]
    public void ClassesAreRefusedUntilADeploymentDeclaresWhichOnesExist()
    {
        const string Markup = "<p class=\"lead\">x</p>";

        // An empty allowlist means no class attribute at all. Reading it as "anything goes" would
        // let an author hang any of the site's own styles off arbitrary content.
        _sanitizer.Sanitize(Markup, SanitizationProfile.Extended).Should().NotContain("class");

        var configured = new SanitizationOptions();
        configured.AllowedCssClasses.Add("lead");

        new SanitizationService(configured).Sanitize(Markup, SanitizationProfile.Extended)
            .Should().Contain("class=\"lead\"");
    }

    [Fact]
    public void AnUnlistedClassIsDroppedWhileAListedOneSurvives()
    {
        var options = new SanitizationOptions();
        options.AllowedCssClasses.Add("lead");

        var sanitized = new SanitizationService(options).Sanitize(
            "<p class=\"lead admin-only\">x</p>",
            SanitizationProfile.Extended);

        sanitized.Should().Contain("lead").And.NotContain("admin-only");
    }

    [Fact]
    public void ClassesAreNeverAllowedUnderBasic()
    {
        var options = new SanitizationOptions();
        options.AllowedCssClasses.Add("lead");

        new SanitizationService(options).Sanitize("<p class=\"lead\">x</p>", SanitizationProfile.Basic)
            .Should().NotContain("class");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyInputSanitizesToEmptyOutput(string? html)
    {
        _sanitizer.Sanitize(html, SanitizationProfile.Basic).Should().BeEmpty();
        _sanitizer.SanitizeWithReport(html, SanitizationProfile.Basic).RemovedAnything.Should().BeFalse();
    }

    [Fact]
    public void CleanMarkupIsReportedAsUnchanged()
    {
        var result = _sanitizer.SanitizeWithReport("<p>Clean</p>", SanitizationProfile.Basic);

        result.Html.Should().Be("<p>Clean</p>");
        result.RemovedAnything.Should().BeFalse();
    }

    [Fact]
    public void TheReportAndTheFastPathAgree()
    {
        // SanitizeWithReport builds its own sanitizer so that a caller's list cannot be written to
        // from another thread's call. That is two construction paths, and two construction paths
        // that drift would put the editor's preview out of step with what actually gets stored.
        foreach (var payload in XssCorpus.All)
        {
            foreach (var profile in (SanitizationProfile[])
                [SanitizationProfile.Basic, SanitizationProfile.Extended, SanitizationProfile.Developer])
            {
                _sanitizer.SanitizeWithReport(payload.Payload, profile).Html
                    .Should().Be(
                        _sanitizer.Sanitize(payload.Payload, profile),
                        $"the reporting path must not sanitize {payload.Name} differently under {profile}");
            }
        }
    }
}
