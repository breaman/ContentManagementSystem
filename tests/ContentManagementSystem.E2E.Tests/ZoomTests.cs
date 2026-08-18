using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The backoffice at 200% browser zoom (task P6-38, acceptance criterion P6 #14).
/// </summary>
/// <remarks>
/// <strong>200% zoom is a viewport of half the width, not a screenshot scaled up.</strong> A browser
/// zoomed to 200% on a 1280-pixel display reports 640 CSS pixels, so the failure it produces is a
/// layout one: a table that will not narrow, a fixed width in a panel, a toolbar that pushes the page
/// sideways. WCAG 1.4.10 puts it plainly — content must reflow without a horizontal scrollbar, because
/// scrolling in two directions to read one line is what makes a zoomed page unusable rather than
/// merely large.
/// <para>
/// The screens are rendered statically and then laid out with the site's own stylesheet, so what is
/// being measured is the CSS rather than a mock-up of it. Wide content that <em>should</em> scroll —
/// a URL table, a diff — is allowed to, as long as it scrolls inside its own container: that is what
/// Bootstrap's <c>table-responsive</c> is for, and it is the difference between one element scrolling
/// and the page doing it.
/// </para>
/// </remarks>
public class ZoomTests
{
    /// <summary>The viewport a 1280-pixel display reports at 200% zoom.</summary>
    private const int ZoomedWidth = 640;

    /// <summary>The height a 1024-pixel display reports at the same zoom.</summary>
    private const int ZoomedHeight = 512;

    [Test]
    [MethodDataSource(typeof(PageScreenAccessibilityTests), nameof(PageScreenAccessibilityTests.Screens))]
    public async Task ABackofficeScreenReflowsAtTwoHundredPercentZoom(
        string description,
        Type component,
        Dictionary<string, object?> parameters,
        string expected)
    {
        var html = await BackofficeScreens.RenderAsync(component, parameters);

        html.Should().Contain(expected, "the pass must judge a loaded screen, not its placeholder");

        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = ZoomedWidth, Height = ZoomedHeight },
        });

        await SiteStyles.ServeAsync(page, html);
        await page.GotoAsync($"{SiteStyles.Origin}/");

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(
            0,
            "the {0} screen overflows its viewport by {1}px at 200% zoom, so reading one line means " +
            "scrolling in two directions (WCAG 1.4.10)",
            description,
            overflow);
    }

    [Test]
    public async Task TheMeasurementItselfCatchesSomethingTooWide()
    {
        // A negative control. The assertion above is the kind that passes for the wrong reason — a
        // stylesheet that never loaded, a document that rendered nothing — so something known to be
        // too wide has to be shown to fail it.
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = ZoomedWidth, Height = ZoomedHeight },
        });

        await SiteStyles.ServeAsync(page, """<div style="width: 1600px">Too wide to reflow</div>""");
        await page.GotoAsync($"{SiteStyles.Origin}/");

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeGreaterThan(
            0,
            "a measurement that cannot fail is not a measurement");
    }
}
