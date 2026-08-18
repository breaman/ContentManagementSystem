using ContentManagementSystem.Core.Security;

using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// The rules a profile is not permitted to relax (task P1-18, spec section 20.2).
/// </summary>
/// <remarks>
/// These assert against the allowlists themselves rather than against sanitized output. The
/// difference matters: <see cref="XssCorpusTests"/> asks "did this payload survive", which is a
/// question about one payload, while this file asks "could any payload of that shape survive", which
/// is a question about the policy. A widening that no corpus entry happens to exercise is caught
/// here and nowhere else.
/// </remarks>
public class SanitizationPolicyTests
{
    /// <summary>
    /// Elements that carry script, style, metadata, or another document.
    /// </summary>
    /// <remarks>
    /// The spec's cross-profile rules name <c>script</c> and <c>style</c> explicitly. The rest are
    /// the same category — an element whose content or attributes are interpreted as something other
    /// than prose — and admitting any of them re-opens gap #11.
    /// </remarks>
    private static readonly string[] Executable =
    [
        "script", "style", "noscript", "template", "xmp", "plaintext",
        "base", "meta", "link", "title", "head", "html", "body",
        "object", "embed", "applet", "param", "frame", "frameset",
        "svg", "math",
        "form", "input", "button", "select", "textarea", "keygen",
    ];

    public static IEnumerable<SanitizationProfile> Profiles =>
    [
        SanitizationProfile.Basic,
        SanitizationProfile.Extended,
        SanitizationProfile.Developer,
    ];

    [Test]
    [MethodDataSource(nameof(Profiles))]
    public void NoProfileAllowsAnExecutableElement(SanitizationProfile profile) =>
        SanitizationPolicy.TagsFor(profile).Should().NotIntersectWith(Executable);

    [Test]
    [MethodDataSource(nameof(Profiles))]
    public void NoProfileAllowsAnEventHandlerAttribute(SanitizationProfile profile) =>
        SanitizationPolicy.AttributesFor(profile).Should().OnlyContain(
            attribute => !attribute.StartsWith("on", StringComparison.OrdinalIgnoreCase));

    [Test]
    [MethodDataSource(nameof(Profiles))]
    public void NoProfileAllowsAnAttributeThatCarriesADocument(SanitizationProfile profile) =>
        SanitizationPolicy.AttributesFor(profile).Should().NotIntersectWith(
            ["srcdoc", "http-equiv", "formaction", "xlink:href", "content"]);

    [Test]
    public void TheSchemeAllowlistIsTheSpecsList()
    {
        // data is present so that the inline-image case can be reached at all; SanitizationService
        // is what decides whether a given data: URI survives. Anything else appearing here would be
        // a scheme a browser may navigate.
        SanitizationPolicy.AllowedSchemes.Should().BeEquivalentTo(
            ["http", "https", "mailto", "tel", "data"]);
    }

    [Test]
    public void NoCssPropertyCanTakeAnElementOutOfTheDocumentFlowOrFetchAUrl()
    {
        // A different threat from script injection and not addressed by the tag allowlist: an
        // absolutely positioned overlay is a clickjacking surface, and a property that takes a url()
        // is a tracking beacon that fires on render.
        SanitizationPolicy.AllowedCssProperties.Should().NotIntersectWith(
            [
                "position", "z-index", "transform", "opacity", "pointer-events",
                "background", "background-image", "content", "behavior", "-moz-binding",
                "filter", "clip-path", "mix-blend-mode",
            ]);
    }

    [Test]
    public void NoInlineImageMayDeclareADocumentFormat()
    {
        // SVG is a document that can carry script, so it stays off this list whatever answer open
        // question Q7 gets for uploaded files.
        SanitizationPolicy.AllowedDataUriMediaTypes.Should().OnlyContain(
            mediaType => mediaType.StartsWith("image/", StringComparison.Ordinal));

        SanitizationPolicy.AllowedDataUriMediaTypes.Should().NotContain("image/svg+xml");
    }

    [Test]
    [Arguments(SanitizationProfile.Extended)]
    [Arguments(SanitizationProfile.Developer)]
    public void TheProfilesNestSoAWiderOneNeverSubtracts(SanitizationProfile wider)
    {
        // What makes "every rule Basic enforces, the wider two enforce as well" true by
        // construction. A profile that dropped a tag on the way up would be a profile whose content
        // cannot be re-sanitized under a narrower one without loss.
        SanitizationPolicy.TagsFor(wider).Should()
            .Contain(SanitizationPolicy.TagsFor(SanitizationProfile.Basic));

        SanitizationPolicy.AttributesFor(wider).Should()
            .Contain(SanitizationPolicy.AttributesFor(SanitizationProfile.Basic));
    }

    [Test]
    public void OnlyDeveloperAllowsAnEmbed()
    {
        SanitizationPolicy.TagsFor(SanitizationProfile.Basic).Should().NotContain("iframe");
        SanitizationPolicy.TagsFor(SanitizationProfile.Extended).Should().NotContain("iframe");
        SanitizationPolicy.TagsFor(SanitizationProfile.Developer).Should().Contain("iframe");
    }

    [Test]
    public void TheDefaultFrameHostsAreTheEmbedProvidersTheSpecNames()
    {
        // The wider point in spec section 20.5 is that editor-supplied embeds should go through a
        // dedicated embed block type. This list exists so the html field type does not become the
        // way around that, which means it should stay short.
        new SanitizationOptions().AllowedIframeHosts.Should().OnlyContain(
            host => host.EndsWith("youtube.com", StringComparison.Ordinal) ||
                    host.EndsWith("youtube-nocookie.com", StringComparison.Ordinal) ||
                    host.EndsWith("vimeo.com", StringComparison.Ordinal));
    }

    [Test]
    public void ADeploymentDeclaresNoClassesByDefault()
    {
        new SanitizationOptions().AllowedCssClasses.Should().BeEmpty();
    }
}
