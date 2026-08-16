using ContentManagementSystem.Core.Media.Upload;

namespace ContentManagementSystem.Core.Tests.Media;

/// <summary>
/// The strict SVG profile (task P5-06, spec section 13.3 step 5).
/// </summary>
/// <remarks>
/// Only reached when a deployment opts into <see cref="SvgUploadPolicy.Sanitize"/>; the default is
/// to refuse SVG entirely, which is the recommended answer to Q7. These tests exist so that a site
/// which does opt in has one implementation whose behaviour is pinned rather than assumed.
/// <para>
/// Every case is a published SVG XSS technique. Each asserts on the <em>output</em> — what would be
/// stored and later served — rather than on a removal report, because a removal that was reported
/// and not performed is exactly the failure that matters.
/// </para>
/// </remarks>
public class SvgSanitizerTests
{
    private static async Task<string> Sanitize(string svg)
    {
        var result = await SvgSanitizer.SanitizeAsync(svg, TestContext.Current.CancellationToken);

        return result.Svg ?? string.Empty;
    }

    [Fact]
    public async Task ADrawingSurvives()
    {
        var output = await Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="#f00"/></svg>""");

        output.Should().Contain("<rect").And.Contain("fill=\"#f00\"");
    }

    [Fact]
    public async Task AScriptIsRemovedWithItsContents()
    {
        var output = await Sanitize("""<svg><script>alert(1)</script><rect width="1" height="1"/></svg>""");

        // Not merely unwrapped: unwrapping would leave the source as a text node, which several
        // renderers then re-parse.
        output.Should().NotContain("script").And.NotContain("alert");
    }

    [Fact]
    public async Task AnEventHandlerIsRemoved()
    {
        var output = await Sanitize("""<svg onload="alert(1)"><rect onclick="alert(2)" width="1" height="1"/></svg>""");

        output.Should().NotContain("onload").And.NotContain("onclick").And.NotContain("alert");
    }

    [Fact]
    public async Task AForeignObjectIsRemovedWithTheHtmlInsideIt()
    {
        var output = await Sanitize(
            """<svg><foreignObject><body xmlns="http://www.w3.org/1999/xhtml"><img src=x onerror="alert(1)"/></body></foreignObject><rect width="1" height="1"/></svg>""");

        output.Should().NotContain("foreignObject").And.NotContain("onerror").And.NotContain("<img");
    }

    [Fact]
    public async Task AnExternalReferenceIsRemoved()
    {
        var output = await Sanitize(
            """<svg><image href="https://evil.test/x.svg"/><use href="https://evil.test/x.svg#a"/><rect width="1" height="1"/></svg>""");

        output.Should().NotContain("evil.test").And.NotContain("<image").And.NotContain("<use");
    }

    [Fact]
    public async Task AnXlinkHrefIsRemovedDespiteItsPrefix()
    {
        var output = await Sanitize(
            """<svg xmlns:xlink="http://www.w3.org/1999/xlink"><rect xlink:href="javascript:alert(1)" width="1" height="1"/></svg>""");

        // A check that only knew the unprefixed name would let this through — the namespaced
        // spelling is a different attribute name and the same capability.
        output.Should().NotContain("javascript:").And.NotContain("href");
    }

    [Fact]
    public async Task AnAnimateElementIsRemoved()
    {
        var output = await Sanitize(
            """<svg><rect width="1" height="1"><animate attributeName="href" to="javascript:alert(1)"/></rect></svg>""");

        // Animation can rewrite another element's attributes after load, which turns an inert
        // document into a live one.
        output.Should().NotContain("animate").And.NotContain("javascript:");
    }

    [Fact]
    public async Task AStyleElementIsRemoved()
    {
        var output = await Sanitize(
            """<svg><style>@import url(https://evil.test/x.css)</style><rect width="1" height="1"/></svg>""");

        output.Should().NotContain("style").And.NotContain("evil.test");
    }

    [Fact]
    public async Task AnAnchorIsRemoved()
    {
        var output = await Sanitize("""<svg><a href="javascript:alert(1)"><rect width="1" height="1"/></a></svg>""");

        output.Should().NotContain("javascript:");
    }

    [Fact]
    public async Task AScriptNestedInsideAnUnknownWrapperIsRemoved()
    {
        var output = await Sanitize(
            """<svg><unknownthing><script>alert(1)</script></unknownthing><rect width="1" height="1"/></svg>""");

        // The unwrapping path is the one where a sanitizer most easily promotes something it never
        // re-checked.
        output.Should().NotContain("script").And.NotContain("alert");
    }

    [Fact]
    public async Task ADocumentWhoseOnlyContentWasHostileIsRefusedOutright()
    {
        var result = await SvgSanitizer.SanitizeAsync(
            """<svg><script>alert(1)</script></svg>""", TestContext.Current.CancellationToken);

        // Storing an empty <svg> would be storing nothing while telling the editor their logo
        // uploaded successfully.
        result.Svg.Should().BeNull();
    }

    [Fact]
    public async Task SomethingThatIsNotAnSvgIsRefused()
    {
        var result = await SvgSanitizer.SanitizeAsync(
            "<html><body>hello</body></html>", TestContext.Current.CancellationToken);

        result.Svg.Should().BeNull();
    }

    [Fact]
    public async Task RemovalsAreReported()
    {
        var result = await SvgSanitizer.SanitizeAsync(
            """<svg onload="x()"><script>y()</script><rect width="1" height="1"/></svg>""",
            TestContext.Current.CancellationToken);

        result.RemovedElements.Should().Contain("script");
        result.RemovedAttributes.Should().Contain("onload");
    }
}
