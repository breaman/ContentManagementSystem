using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// What "nothing hostile survived" means, asserted over a delivered page (task P9-06).
/// </summary>
/// <remarks>
/// The unit corpus asserts over the sanitizer's output; this asserts over the bytes a browser
/// receives. Between the two sit two things that could undo the first: the render-time sanitization
/// pass (ADR-0008), and the component renderer that writes the result into a document. A field
/// renderer that emitted its value as a <c>MarkupString</c> without going through the pipeline would
/// pass every test in the unit suite.
/// <para>
/// <strong>Only the content region is inspected.</strong> A page legitimately contains
/// <c>&lt;title&gt;</c>, <c>&lt;meta&gt;</c>, <c>&lt;link&gt;</c>, and a <c>&lt;head&gt;</c>, all of
/// which the fragment-level rules forbid outright — so scoping to <c>main.cms-delivery</c> is what
/// makes the same rules mean the same thing here. It is also the honest scope: the shell is written
/// by this repository and the region is written by whoever typed into a zone.
/// </para>
/// </remarks>
internal static class DeliveredMarkupAssertions
{
    /// <summary>The element wrapping everything an author contributed to a public page.</summary>
    public const string ContentSelector = "main.cms-delivery";

    private static readonly HtmlParser Parser = new();

    /// <summary>Elements no authored content may render into a public page.</summary>
    /// <remarks>
    /// Stated here rather than derived from <c>SanitizationPolicy</c>, for the reason the unit
    /// corpus restates its own list: a check that says "the output conforms to the profile" passes
    /// for any output at all once somebody widens the profile.
    /// </remarks>
    private static readonly string[] NeverRendered =
    [
        "script", "style", "noscript", "template", "xmp", "plaintext",
        "base", "meta", "link", "title", "head",
        "object", "embed", "applet", "param", "frame", "frameset",
        "svg", "math",
        "form", "input", "button", "select", "textarea",
    ];

    /// <summary>
    /// Attributes no authored content may render, whatever else it carries.
    /// </summary>
    /// <remarks>
    /// <c>srcdoc</c> is an entire HTML document smuggled through one attribute, which is why an
    /// <c>iframe</c> allowlist that only checks <c>src</c> is not enough on its own.
    /// </remarks>
    private static readonly string[] NeverRenderedAttributes =
        ["srcdoc", "http-equiv", "formaction", "xlink:href"];

    /// <summary>Attribute values that navigate or execute, once whitespace is squeezed out.</summary>
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
    /// Asserts that the content region of a delivered document carries nothing executable.
    /// </summary>
    /// <param name="html">The whole response body.</param>
    /// <param name="because">What was being stored when this page was produced.</param>
    /// <exception cref="ArgumentNullException"><paramref name="html"/> is null.</exception>
    public static void AssertNothingExecutable(string html, string because)
    {
        ArgumentNullException.ThrowIfNull(html);

        var document = Parser.ParseDocument(html);
        var content = document.QuerySelector(ContentSelector);

        content.Should().NotBeNull($"the delivered page has a content region ({because})");

        foreach (var element in content!.QuerySelectorAll("*"))
        {
            NeverRendered.Should().NotContain(element.LocalName, because);

            foreach (var attribute in element.Attributes)
            {
                // Event handlers, by prefix rather than by list. A browser dispatches every on*
                // attribute it knows about, and the list is longer than anyone remembers.
                attribute.Name.Should().NotStartWith("on", because);

                NeverRenderedAttributes.Should().NotContain(attribute.Name, because);

                var squeezed = new string(attribute.Value
                    .Where(character => !char.IsWhiteSpace(character) && !char.IsControl(character))
                    .ToArray())
                    .ToLowerInvariant();

                // Squeezed first, because jav&#x09;ascript: reaches the DOM as java\tscript: and a
                // browser navigates it. Checking the raw value would miss every entry on the list.
                foreach (var forbidden in ForbiddenInAttributeValues)
                {
                    squeezed.Should().NotContain(forbidden, because);
                }
            }
        }

        // An iframe survives only under Developer and only to an allowlisted host, and the delivered
        // page is where that stops being a claim about a string.
        foreach (var frame in content.QuerySelectorAll("iframe"))
        {
            var source = frame.GetAttribute("src") ?? string.Empty;

            source.Should().StartWith("https://", because);
        }
    }
}
