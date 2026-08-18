using ContentManagementSystem.Core.Content.Markdown;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Tests.Security;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Content.Markdown;

/// <summary>
/// The markdown pipeline (task P1-19, acceptance criterion P1 #7).
/// </summary>
/// <remarks>
/// Two things are under test and they pull in opposite directions: markdown has to render as an
/// author expects, and its output has to be as untrusted as anything else that reaches a page.
/// CommonMark passes raw HTML through by design, so the second is not a hypothetical.
/// </remarks>
public class MarkdownRendererTests
{
    private readonly IMarkdownRenderer _renderer = new MarkdownRenderer(new SanitizationService());

    [Test]
    public void CommonMarkRendersAsExpected()
    {
        var html = _renderer.ToHtml(
            """
            ## Why teams choose us

            We help teams **ship faster** with *less* toil.

            - One
            - Two

            > Quoted

            [link](https://example.test/x)
            """,
            SanitizationProfile.Basic);

        SanitizationAssertions.TagNames(html).Should().Equal(
            "h2", "p", "strong", "em", "ul", "li", "li", "blockquote", "p", "p", "a");
    }

    [Test]
    public void PipeTablesRenderUnderExtended()
    {
        var html = _renderer.ToHtml(
            """
            | Plan | Price |
            |------|-------|
            | Team | $10   |
            """,
            SanitizationProfile.Extended);

        SanitizationAssertions.TagNames(html).Should().Contain(["table", "thead", "tbody", "tr", "th", "td"]);
    }

    [Test]
    public void ABareUrlIsAutoLinked()
    {
        var html = _renderer.ToHtml("Visit https://example.test/x today.", SanitizationProfile.Basic);

        html.Should().Contain("<a href=\"https://example.test/x\"");
    }

    [Test]
    public void ATopLevelHeadingIsUnwrappedRatherThanKept()
    {
        var html = _renderer.ToHtml("# Page title", SanitizationProfile.Basic);

        // h1 belongs to the page title, not to body content, so no profile allows it. The words
        // survive because unknown elements are unwrapped — worth pinning, because the alternative
        // reading of the allowlist silently deletes the first line of every document written by
        // someone used to writing '#'.
        SanitizationAssertions.TagNames(html).Should().BeEmpty();
        html.Should().Contain("Page title");
    }

    [Test]
    public void RawHtmlInMarkdownIsSanitized()
    {
        var html = _renderer.ToHtml(
            """
            Before

            <script>alert('XSS')</script><img src=x onerror=alert('XSS')>

            After
            """,
            SanitizationProfile.Basic);

        // richText stores markdown exactly as authored and does not sanitize it on write, so this
        // conversion is the only thing between a stored payload and a browser.
        SanitizationAssertions.AssertNeutralized(html, SanitizationProfile.Basic);
        html.Should().NotContain("alert");
    }

    [Test]
    public void AnHtmlBlockInMarkdownIsSanitizedToo()
    {
        // A block-level HTML chunk takes a different path through Markdig than an inline one, and
        // it is the path an editor's pasted embed actually takes.
        var html = _renderer.ToHtml(
            """
            <div onclick="alert('XSS')">
              <iframe src="https://evil.test/embed"></iframe>
            </div>
            """,
            SanitizationProfile.Developer);

        SanitizationAssertions.AssertNeutralized(html, SanitizationProfile.Developer);
    }

    [Test]
    [Arguments(SanitizationProfile.Basic)]
    [Arguments(SanitizationProfile.Extended)]
    [Arguments(SanitizationProfile.Developer)]
    public void EveryXssCorpusPayloadIsNeutralizedThroughTheMarkdownPathToo(SanitizationProfile profile)
    {
        // The corpus is aimed at the HTML path, but markdown is a second way into the same renderer
        // and CommonMark's raw-HTML passthrough is exactly the door it opens.
        foreach (var payload in XssCorpus.All)
        {
            var html = _renderer.ToHtml(payload.Payload, profile);

            SanitizationAssertions.AssertNeutralized(html, profile);
        }
    }

    [Test]
    public void MarkdownLinkSyntaxCannotSmuggleAScheme()
    {
        var html = _renderer.ToHtml("[click](javascript:alert('XSS'))", SanitizationProfile.Basic);

        html.Should().NotContain("javascript:");
    }

    [Test]
    public void ThePreviewPathAndTheDeliveryPathProduceIdenticalHtml()
    {
        // Acceptance criterion P1 #7. There is one implementation, so the risk is not two pipelines
        // but two entry points into one — the preview reads ToHtmlWithReport for the strip warning
        // while delivery reads ToHtml, and those run through different sanitizer instances.
        foreach (var payload in XssCorpus.All)
        {
            foreach (var profile in (SanitizationProfile[])
                [SanitizationProfile.Basic, SanitizationProfile.Extended, SanitizationProfile.Developer])
            {
                _renderer.ToHtmlWithReport(payload.Payload, profile).Html
                    .Should().Be(
                        _renderer.ToHtml(payload.Payload, profile),
                        $"preview and delivery must agree on {payload.Name} under {profile}");
            }
        }
    }

    [Test]
    public void ThePreviewReportsWhatDeliveryWillStrip()
    {
        var result = _renderer.ToHtmlWithReport(
            "Some prose.\n\n<iframe src=\"https://evil.test/x\"></iframe>",
            SanitizationProfile.Developer);

        // This is what lets the editor warn before the save rather than after it.
        result.RemovedAnything.Should().BeTrue();
        result.Removals.Should().Contain(removal => removal.Name == "iframe");
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    public void EmptySourceRendersToEmptyHtml(string? markdown)
    {
        _renderer.ToHtml(markdown, SanitizationProfile.Basic).Should().BeEmpty();
        _renderer.ToHtmlWithReport(markdown, SanitizationProfile.Basic).Html.Should().BeEmpty();
    }
}
