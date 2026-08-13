using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

using ContentManagementSystem.Core.Security;

using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// What "neutralized" means, asserted over the sanitizer's output (task P1-20).
/// </summary>
/// <remarks>
/// The assertion re-parses the sanitized markup and inspects the resulting DOM, rather than looking
/// for forbidden substrings in the text. That is the whole point. A substring check passes for
/// output that contains no literal <c>&lt;script&gt;</c> but that a browser re-parses into one —
/// which is exactly what the mutation-XSS payloads in <see cref="XssCorpus"/> do, and a sanitizer
/// that cleans a DOM and then serializes it badly is a real and repeatedly observed failure.
/// <para>
/// The invariants are stated against <see cref="SanitizationPolicy"/> rather than against a list
/// restated here. A profile that gains a tag gains it in one place; a profile that gains a tag it
/// should not have gained is a review problem, not something a duplicated list would catch.
/// </para>
/// </remarks>
internal static class SanitizationAssertions
{
    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// Substrings no surviving attribute value may contain, once whitespace and control characters
    /// are stripped out of it.
    /// </summary>
    /// <remarks>
    /// The stripping is what makes this worth checking at all: <c>jav&amp;#x09;ascript:</c> and
    /// <c>java\nscript:</c> reach the DOM as <c>java\tscript:</c> and <c>java\nscript:</c>, and a
    /// browser navigates both. Checking the raw value would miss every one of them.
    /// </remarks>
    private static readonly string[] ForbiddenInAttributeValues =
    [
        "javascript:",
        "vbscript:",
        "livescript:",
        "data:text/html",
        "expression(",
        "behavior:",
        "-moz-binding",
    ];

    /// <summary>
    /// Elements no profile may ever permit, whatever <see cref="SanitizationPolicy"/> says.
    /// </summary>
    /// <remarks>
    /// Stated here rather than derived from the policy on purpose, and it is the difference between
    /// a merge gate and a tautology. Every other invariant in this file is "the output conforms to
    /// the profile", which passes for any output at all once someone widens the profile — a change
    /// that adds <c>script</c> to the allowlist would satisfy a conformance check while defeating
    /// the entire suite. These names are the spec's own cross-profile rules (section 20.2), so a
    /// commit that admits one of them fails here regardless of what it did to the policy.
    /// <para>
    /// <c>iframe</c> is absent because <c>Developer</c> legitimately allows it against a host
    /// allowlist; <see cref="AssertNeutralized"/> checks that case separately.
    /// </para>
    /// </remarks>
    private static readonly string[] NeverAllowed =
    [
        "script", "style", "noscript", "template", "xmp", "plaintext",
        "base", "meta", "link", "title", "head",
        "object", "embed", "applet", "param", "frame", "frameset",
        "svg", "math",
        "form", "input", "button", "select", "textarea",
    ];

    /// <summary>Attributes no profile may ever permit.</summary>
    /// <remarks>
    /// <c>srcdoc</c> is an entire HTML document smuggled through one attribute, and it is the reason
    /// an <c>iframe</c> allowlist that only checks <c>src</c> is not enough on its own.
    /// </remarks>
    private static readonly string[] NeverAllowedAttributes = ["srcdoc", "http-equiv", "formaction", "xlink:href"];

    /// <summary>
    /// Asserts that nothing outside the profile's allowlist survived, and that nothing that did
    /// survive can execute.
    /// </summary>
    /// <param name="html">The sanitizer's output.</param>
    /// <param name="profile">The profile that produced it.</param>
    /// <param name="alsoAllowedAttributes">Attributes a deployment option permits beyond the profile.</param>
    public static void AssertNeutralized(
        string html,
        SanitizationProfile profile,
        IReadOnlyCollection<string>? alsoAllowedAttributes = null)
    {
        var allowedTags = SanitizationPolicy.TagsFor(profile);
        var allowedAttributes = SanitizationPolicy.AttributesFor(profile);

        foreach (var element in Elements(html))
        {
            NeverAllowed.Should().NotContain(element.LocalName,
                $"no profile may permit '<{element.LocalName}>', and one survived in: {html}");

            allowedTags.Should().Contain(element.LocalName,
                $"'<{element.LocalName}>' survived the {profile} profile in: {html}");

            AssertFrameIsAllowlisted(element, profile, html);

            foreach (var attribute in element.Attributes)
            {
                attribute.Name.Should().NotStartWith("on",
                    $"an event handler survived on <{element.LocalName}> in: {html}");

                NeverAllowedAttributes.Should().NotContain(attribute.Name,
                    $"no profile may permit '{attribute.Name}', and one survived on " +
                    $"<{element.LocalName}> in: {html}");

                var permitted =
                    allowedAttributes.Contains(attribute.Name) ||
                    alsoAllowedAttributes?.Contains(attribute.Name) is true ||
                    (profile is SanitizationProfile.Developer &&
                        attribute.Name.StartsWith("data-", StringComparison.Ordinal));

                permitted.Should().BeTrue(
                    $"'{attribute.Name}' survived on <{element.LocalName}> under {profile} in: {html}");

                AssertValueCannotExecute(element, attribute, html);
            }
        }

        Comments(html).Should().BeEmpty(
            $"comments are removed under every profile, and a surviving one can hide markup: {html}");
    }

    /// <summary>Elements in the body of a parsed fragment.</summary>
    /// <param name="html">The markup to parse.</param>
    public static IEnumerable<IElement> Elements(string html)
    {
        using var document = Parse(html);

        return document.Body is null ? [] : [.. document.Body.QuerySelectorAll("*")];
    }

    /// <summary>Comment nodes anywhere in a parsed fragment.</summary>
    /// <param name="html">The markup to parse.</param>
    public static IReadOnlyList<string> Comments(string html)
    {
        using var document = Parse(html);

        return document.Body is null ? [] : [.. Descendants(document.Body).OfType<IComment>().Select(c => c.Data)];
    }

    /// <summary>The local names of the elements in a fragment, in document order.</summary>
    /// <param name="html">The markup to parse.</param>
    public static IReadOnlyList<string> TagNames(string html) => [.. Elements(html).Select(e => e.LocalName)];

    /// <summary>
    /// Asserts that a surviving <c>iframe</c> frames an allowlisted host over HTTPS.
    /// </summary>
    /// <param name="element">The element to check.</param>
    /// <param name="profile">The profile that produced the markup.</param>
    /// <param name="html">The markup, for the failure message.</param>
    /// <remarks>
    /// The one element whose safety is a property of its <c>src</c> rather than of its name, so the
    /// tag allowlist cannot decide it. Checked against the service's defaults, which is what the
    /// corpus runs under.
    /// </remarks>
    private static void AssertFrameIsAllowlisted(IElement element, SanitizationProfile profile, string html)
    {
        if (!element.LocalName.Equals("iframe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        profile.Should().Be(SanitizationProfile.Developer,
            $"only the Developer profile allows an iframe, and one survived in: {html}");

        var source = element.GetAttribute("src");

        source.Should().NotBeNullOrEmpty($"an iframe with no src survived in: {html}");

        Uri.TryCreate(source, UriKind.Absolute, out var uri).Should().BeTrue(
            $"an iframe with a non-absolute src survived in: {html}");

        uri!.Scheme.Should().Be(Uri.UriSchemeHttps, $"an iframe over plain HTTP survived in: {html}");

        new SanitizationOptions().AllowedIframeHosts.Should().Contain(uri.Host,
            $"an iframe pointing at an unlisted host survived in: {html}");
    }

    private static void AssertValueCannotExecute(IElement element, IAttr attribute, string html)
    {
        var collapsed = Collapse(attribute.Value);

        foreach (var forbidden in ForbiddenInAttributeValues)
        {
            collapsed.Should().NotContain(forbidden,
                $"'{attribute.Name}' on <{element.LocalName}> still carries '{forbidden}' in: {html}");
        }
    }

    /// <summary>
    /// Lower-cases a value and drops the whitespace and control characters a browser ignores when it
    /// reads a URL scheme.
    /// </summary>
    /// <param name="value">The attribute value.</param>
    private static string Collapse(string value) =>
        string.Concat(value.Where(c => !char.IsWhiteSpace(c) && !char.IsControl(c))).ToLowerInvariant();

    private static IHtmlDocument Parse(string html) =>
        Parser.ParseDocument("<!doctype html><html><body>" + html);

    private static IEnumerable<INode> Descendants(INode node)
    {
        foreach (var child in node.ChildNodes)
        {
            yield return child;

            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
