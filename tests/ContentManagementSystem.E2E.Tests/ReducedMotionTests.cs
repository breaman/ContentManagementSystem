using ContentManagementSystem.Client.Components.Admin.Dashboard;

using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// <c>prefers-reduced-motion</c>, on both front doors (task P9-09).
/// </summary>
/// <remarks>
/// The setting is not a preference about taste. For a reader with a vestibular disorder an animation
/// that keeps running after they have asked it to stop causes nausea and disorientation, which is why
/// WCAG 2.2 treats it as a success criterion rather than a nicety.
/// <para>
/// <strong>Asserted by measuring computed style in a browser that is asking for reduced motion</strong>,
/// rather than by grepping the stylesheet for a media query. A query that exists and does not cover
/// the animation somebody added last week passes the grep and fails the reader; a running animation
/// is what the reader experiences, and it is what this measures.
/// </para>
/// </remarks>
public class ReducedMotionTests
{
    /// <summary>
    /// Reports every element still animating or transitioning.
    /// </summary>
    /// <remarks>
    /// <c>animation-name: none</c> is the reliable signal — a stopped animation still reports its
    /// duration in some engines, and a zero duration is not the same as an absent animation. A
    /// transition is caught by its duration, since a transition has no name.
    /// </remarks>
    private const string MovingElements = """
        () => Array.from(document.querySelectorAll('*'))
            .filter(element => {
                const style = getComputedStyle(element);
                const animated = style.animationName !== 'none' &&
                    parseFloat(style.animationDuration) > 0;
                const transitioned = parseFloat(style.transitionDuration) > 0;
                return animated || transitioned;
            })
            .map(element => `${element.tagName.toLowerCase()}.${element.className}`)
        """;

    [Test]
    public async Task NothingInTheBackofficeMovesWhenAReaderHasAskedItNotTo()
    {
        // The dashboard is the screen with motion on it: the save-state indicator spins while a write
        // is in flight, and it is the one animation task P6-39 had to switch off.
        var html = await BackofficeScreens.RenderAsync(typeof(DashboardScreen), []);

        var moving = await MovingAsync(html, wholeDocument: false);

        moving.Should().BeEmpty(
            "the backoffice must hold still for a reader who asked it to. Still moving: {0}",
            string.Join(", ", moving));
    }

    [Test]
    public async Task NothingOnAPublicPageMovesWhenAReaderHasAskedItNotTo()
    {
        var html = await PublicPages.RenderAsync("Pricing", PublicPageAccessibilityTests.FullPage);

        var moving = await MovingAsync(html, wholeDocument: true);

        moving.Should().BeEmpty(
            "the public site must hold still for a reader who asked it to. Still moving: {0}",
            string.Join(", ", moving));
    }

    [Test]
    public async Task TheMeasurementItselfCatchesSomethingThatKeepsMoving()
    {
        // A negative control, for the reason the zoom pass has one: an assertion that everything is
        // still passes just as well against a page that rendered nothing.
        var moving = await MovingAsync(
            """
            <style>@keyframes drift { to { transform: translateX(10px); } }
            .keeps-going { animation: drift 2s linear infinite; }</style>
            <div class="keeps-going">Ignores the setting</div>
            """,
            wholeDocument: false);

        moving.Should().NotBeEmpty("a measurement that cannot fail is not a measurement");
    }

    /// <summary>Loads markup in a browser asking for reduced motion and reports what still moves.</summary>
    /// <param name="html">The markup, or a whole document.</param>
    /// <param name="wholeDocument">Whether <paramref name="html"/> is a complete document.</param>
    /// <returns>One entry per element still animating or transitioning.</returns>
    private static async Task<string[]> MovingAsync(string html, bool wholeDocument)
    {
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        // The browser asks for reduced motion for the whole context, which is what a reader's own
        // operating-system setting produces. Emulating it per page would leave the stylesheet's own
        // media query evaluated against the default.
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ReducedMotion = ReducedMotion.Reduce,
        });

        var page = await context.NewPageAsync();

        if (wholeDocument)
        {
            await SiteStyles.ServeDocumentAsync(page, html);
        }
        else
        {
            await SiteStyles.ServeAsync(page, html);
        }

        await page.GotoAsync($"{SiteStyles.Origin}/");

        return await page.EvaluateAsync<string[]>(MovingElements);
    }
}
