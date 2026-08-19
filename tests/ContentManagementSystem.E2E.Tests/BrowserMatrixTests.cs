using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The public page in every engine NFR-13 covers (task P9-24).
/// </summary>
/// <remarks>
/// NFR-13 names the last two versions of Chrome, Edge, Firefox, and Safari. That is **three engines**:
/// Chrome and Edge are both Blink, and the difference between them is a shell rather than a renderer.
/// Playwright bundles all three — Chromium, Firefox, and WebKit, which is Safari's engine — so what
/// is left uncovered here is Edge's own shell, and nothing in this application's output depends on
/// one.
/// <para>
/// <strong>What is asserted is layout and rendering, not features.</strong> The public site is static
/// HTML and CSS with no script of its own, so "does it work" reduces to "does it come out the same
/// shape" — and the engine-specific failure that actually happens is CSS: a grid or flex rule one
/// engine implements differently, which shows up as content overflowing or collapsing rather than as
/// an error anybody logs.
/// </para>
/// <para>
/// The backoffice is deliberately not in this matrix. It is a WebAssembly application whose browser
/// support is the .NET runtime's, and driving it needs the hosted-app harness that `P6-32` to
/// `P6-34` are also waiting on.
/// </para>
/// </remarks>
public class BrowserMatrixTests
{
    /// <summary>A desktop viewport, wide enough that reflow is not what is being measured.</summary>
    private const int Width = 1280;

    /// <summary>The height that goes with it.</summary>
    private const int Height = 900;

    /// <summary>The three engines behind the four browsers NFR-13 names.</summary>
    public static IEnumerable<string> Engines => ["chromium", "firefox", "webkit"];

    [Test]
    [MethodDataSource(nameof(Engines))]
    public async Task ThePublicPageRendersTheSameShapeInEveryEngine(string engine)
    {
        var html = await PublicPages.RenderAsync("Pricing", PublicPageAccessibilityTests.FullPage);

        PlaywrightBrowsers.EnsureInstalled(engine);

        using var playwright = await Playwright.CreateAsync();

        var type = engine switch
        {
            "firefox" => playwright.Firefox,
            "webkit" => playwright.Webkit,
            _ => playwright.Chromium,
        };

        await using var browser = await type.LaunchAsync();

        var page = await browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = Width, Height = Height },
        });

        await SiteStyles.ServeDocumentAsync(page, html);
        await page.GotoAsync($"{SiteStyles.Origin}/");

        // The content arrived. This matters: a page that rendered nothing passes a "no horizontal
        // scrollbar" check just as well as one that rendered correctly.
        (await page.Locator("h1").First.InnerTextAsync()).Should().Be("Pricing");

        var overflow = await page.EvaluateAsync<int>(
            "() => document.documentElement.scrollWidth - document.documentElement.clientWidth");

        overflow.Should().BeLessThanOrEqualTo(0, "{0} laid the page out wider than its viewport", engine);

        // The table is the element most likely to be laid out differently, and the one an editor is
        // most likely to author. It must be inside the content column rather than pushing past it.
        var table = await page.Locator("table").First.BoundingBoxAsync();

        table.Should().NotBeNull("{0} rendered no table at all", engine);
        table!.Width.Should().BeLessThanOrEqualTo(Width, "{0} rendered the table wider than the viewport", engine);

        // Nothing was left invisible by a font that did not load: an element with no height is the
        // shape a missing web font or a collapsed grid row takes.
        var heading = await page.Locator("h1").First.BoundingBoxAsync();

        heading!.Height.Should().BeGreaterThan(0, "{0} rendered the title with no height", engine);
    }
}
