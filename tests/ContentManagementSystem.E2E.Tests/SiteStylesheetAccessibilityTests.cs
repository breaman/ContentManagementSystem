using ContentManagementSystem.Server.Delivery.Appearance;

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The public accessibility gate, re-run with an administrator-authored stylesheet applied
/// (task P10-17, criterion P10 #7).
/// </summary>
/// <remarks>
/// <strong>Contrast is the failure this feature makes easy to introduce, and it is the one a machine
/// can actually catch.</strong> A brand colour that reads well on a swatch is frequently unreadable
/// as body text, and CSS cannot fail loudly — a stylesheet that makes the site unreadable still
/// returns a perfectly good page (risk R21).
/// <para>
/// The negative control is the point of the file. Without a stylesheet that <em>must</em> fail, a
/// green run proves only that axe was pointed at something; the pair proves it is reading the
/// administrator's CSS rather than the shipped one.
/// </para>
/// </remarks>
public class SiteStylesheetAccessibilityTests
{
    /// <summary>The rule sets the gate enforces, matching the other public passes.</summary>
    private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"];

    /// <summary>A stylesheet of the kind an administrator actually writes: spacing and a heading face.</summary>
    private const string ReasonableCss = """
        .cms-page { --cms-measure: 62ch; }
        .cms-page h1 { letter-spacing: -0.01em; }
        main.cms-delivery { padding-block: 2rem; }
        """;

    /// <summary>
    /// The negative control: light grey text on white, which is roughly 1.6:1.
    /// </summary>
    /// <remarks>
    /// Deliberately the mistake somebody makes rather than an absurd one — this is what "make the
    /// body copy a bit lighter" turns into, and it is exactly what nobody notices until a reader
    /// reports it.
    /// </remarks>
    private const string UnreadableCss = """
        body { background: #ffffff; }
        body, main.cms-delivery, main.cms-delivery p, main.cms-delivery li { color: #dcdcdc; }
        """;

    [Test]
    public async Task ThePublicDocumentStaysAccessibleWithAPublishedStylesheet()
    {
        var failures = await ViolationsAsync(ReasonableCss);

        failures.Should().BeEmpty(
            "an ordinary site stylesheet must not break the page it styles. axe reported: {0}",
            string.Join(" | ", failures));
    }

    [Test]
    public async Task AnUnreadableStylesheetFailsTheGate()
    {
        var failures = await ViolationsAsync(UnreadableCss);

        // The control. If this passes, the gate above is judging a page the administrator's CSS
        // never reached, and its green is worth nothing.
        failures.Should().Contain(
            failure => failure.Contains("color-contrast", StringComparison.Ordinal),
            "the gate has to be able to see what the published stylesheet did to the page");
    }

    [Test]
    public async Task TheDocumentLinksTheAdministratorsStylesheetAfterTheShippedOne()
    {
        var html = await PublicPages.RenderAsync(
            "Pricing",
            PublicPageAccessibilityTests.FullPage,
            SiteStylesheetEndpoint.Path);

        var shipped = html.IndexOf("/css/site.css", StringComparison.Ordinal);
        var custom = html.IndexOf(SiteStylesheetEndpoint.Path, StringComparison.Ordinal);

        // Later rules of equal specificity win, which is the whole mechanism (spec section 30.1).
        // Reversed, the feature silently stops working and nothing else in this file would notice.
        shipped.Should().BeGreaterThanOrEqualTo(0);
        custom.Should().BeGreaterThan(shipped);
    }

    /// <summary>Renders the public page, serves it with the given stylesheet, and runs axe over it.</summary>
    /// <param name="customCss">The administrator's stylesheet.</param>
    /// <returns>One line per violation, empty when there are none.</returns>
    private static async Task<List<string>> ViolationsAsync(string customCss)
    {
        var html = await PublicPages.RenderAsync(
            "Pricing",
            PublicPageAccessibilityTests.FullPage,
            SiteStylesheetEndpoint.Path);

        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        // Served from an origin rather than pushed in with SetContentAsync, because both stylesheet
        // links have to resolve to something — a contrast measurement against a page whose CSS never
        // loaded is a measurement of the browser's defaults.
        await SiteStyles.ServeDocumentAsync(page, html, customCss);
        await page.GotoAsync($"{SiteStyles.Origin}/");

        var results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] },
        });

        results.Passes.Should().NotBeEmpty("axe must actually have run against rendered markup");

        return
        [
            .. results.Violations.Select(violation =>
                $"{violation.Id} ({violation.Impact}): {violation.Help} — " +
                string.Join("; ", violation.Nodes.Select(node => string.Join(",", node.Target)))),
        ];
    }
}
