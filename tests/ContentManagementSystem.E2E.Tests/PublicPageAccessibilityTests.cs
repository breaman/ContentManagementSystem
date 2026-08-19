using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The accessibility gate over what a visitor receives (tasks P9-07, P9-09).
/// </summary>
/// <remarks>
/// The backoffice half of this gate has existed since <c>P6-36</c>. This is the other half of
/// acceptance criterion <c>P9 #2</c>, and it is the half with the wider audience: a backoffice fault
/// affects the people who work here, and a delivery fault affects everyone who reads the site.
/// <para>
/// It judges <c>CmsDeliveryDocument</c> — the real shell — rather than a fragment, because most of
/// what an audit looks at is the shell: the <c>lang</c> attribute, the landmarks, the navigation, and
/// the single <c>h1</c>. The content inside it is deliberately awkward: a table, a nested list, a
/// definition list, and an <c>h2</c>/<c>h3</c> sequence, which is what an editor actually produces.
/// </para>
/// </remarks>
public class PublicPageAccessibilityTests
{
    /// <summary>The rule sets the gate enforces, matching the backoffice suites.</summary>
    private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"];

    /// <summary>Rich text of the kind an editor writes: headings in order, a link, a nested list.</summary>
    public const string Prose = """
        <h2>What each plan includes</h2>
        <p>Every plan carries the same core, and the differences are listed
        <a href="/pricing/comparison">in the full comparison table</a>.</p>
        <h3>Teams</h3>
        <ul><li>Unlimited pages</li><li>Five editors<ul><li>Named seats</li></ul></li></ul>
        """;

    /// <summary>
    /// A table, which only the widest profile keeps.
    /// </summary>
    /// <remarks>
    /// In its own constant and its own zone because it has to be stored as <c>html</c> to survive:
    /// the rich-text profile strips <c>table</c>, so a fixture that put this in a <c>richText</c>
    /// value would be judging a page with no table on it — and would say so nowhere.
    /// <para>
    /// Shared with the zoom pass and the browser matrix, which need the same table for different
    /// reasons: an audit asks whether the header cells say what they head, a reflow measurement asks
    /// whether the whole thing fits in 640 pixels, and the matrix asks whether three engines lay it
    /// out the same way. It was the matrix that noticed it was not being rendered at all.
    /// </para>
    /// </remarks>
    public const string Table = """
        <table>
          <caption>Monthly cost per plan</caption>
          <thead><tr><th scope="col">Plan</th><th scope="col">Cost</th></tr></thead>
          <tbody>
            <tr><th scope="row">Team</th><td>£20</td></tr>
            <tr><th scope="row">Enterprise</th><td>Contact us</td></tr>
          </tbody>
        </table>
        """;

    /// <summary>The zones a full page fixture fills, shared by every public gate.</summary>
    public static string FullPage => string.Join(",\n", new[]
    {
        PublicPages.PlainText("kicker", "Plans"),
        PublicPages.PlainText("standfirst", "What each plan costs and what it includes."),
        PublicPages.RichText("body", Prose),
        PublicPages.Html("embed", Table),
    });

    [Test]
    public async Task ThePublicDocumentHasNoAccessibilityViolations()
    {
        var html = await PublicPages.RenderAsync("Pricing", FullPage);

        // The gate must inspect a rendered page rather than a fallback layout, and it must inspect a
        // page with a real table on it: an unknown template key renders CmsFallbackTemplate, and a
        // table stripped by the wrong profile leaves its caption behind as ordinary text — which is
        // how this fixture passed for a while while judging no table at all.
        html.Should().Contain("data-template=\"article\"")
            .And.Contain("<table")
            .And.Contain("scope=\"col\"");

        var failures = await ViolationsAsync(html);

        failures.Should().BeEmpty(
            "the public site must be usable without a mouse or a screen. axe reported: {0}",
            string.Join(" | ", failures));
    }

    [Test]
    public async Task TheDocumentDeclaresItsLanguage()
    {
        var html = await PublicPages.RenderAsync("Pricing", PublicPages.PlainText("kicker", "Plans"));

        // From SiteSettings.Culture, through the head builder (spec section 28, task P9-10). A screen
        // reader chooses its pronunciation from this, so the failure is one nobody sees and every
        // listener hears — and it is invisible to axe's own html-has-lang rule once any value is set,
        // which is why the value itself is asserted rather than only its presence.
        html.Should().Contain("""<html lang="en-GB">""");
    }

    [Test]
    public async Task ThePageHasExactlyOneTopLevelHeadingAndItIsTheTitle()
    {
        var html = await PublicPages.RenderAsync("Pricing", FullPage);

        // The template owns the h1 and the rich-text profile has no h1 in it, so this is a structural
        // property rather than an editorial habit — and it is what makes the authored h2 that follows
        // the right level rather than a guess.
        System.Text.RegularExpressions.Regex.Matches(html, "<h1[ >]").Should().ContainSingle();
        html.Should().Contain(">Pricing</h1>");
    }

    [Test]
    public async Task NavigationIsALabelledLandmarkWithReachableEntries()
    {
        var html = await PublicPages.RenderAsync("Pricing", PublicPages.PlainText("kicker", "Plans"));

        // The site's menu is the shell's, not a template's — a template that had to render it is a
        // template that can forget to (task P8-17). A reader jumping by landmark needs it named,
        // because "navigation" on its own says nothing when a page has two.
        html.Should().Contain("<nav").And.Contain("aria-label=\"Main\"");
        html.Should().Contain("href=\"/products\"").And.Contain("href=\"/pricing\"");
    }

    [Test]
    public async Task AZoneNoEditorFilledRendersNothingRatherThanAnEmptyLandmark()
    {
        var html = await PublicPages.RenderAsync("Pricing", PublicPages.PlainText("kicker", "Plans"));

        // Every unfilled zone in the article template still renders its wrapper, and axe is the thing
        // that notices when one of those wrappers is a landmark or a heading with nothing in it. This
        // asserts the gate above ran against that case rather than only against a full page.
        var failures = await ViolationsAsync(html);

        failures.Should().BeEmpty(
            "a page with most of its zones empty must still be accessible. axe reported: {0}",
            string.Join(" | ", failures));
    }

    /// <summary>Runs axe over a complete document and returns what it found.</summary>
    /// <param name="html">The document.</param>
    /// <returns>One line per violation, empty when there are none.</returns>
    private static async Task<List<string>> ViolationsAsync(string html)
    {
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        await page.SetContentAsync(html);

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
