using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Proves the Playwright harness is installed and drivable (task P0-11).
/// </summary>
/// <remarks>
/// Browser journeys against the real application arrive in later phases. This suite only confirms
/// that the driver and a browser binary are present, so a later E2E failure points at the
/// application rather than at missing tooling. Browsers install themselves on first run — see
/// <see cref="PlaywrightBrowsers"/>.
/// </remarks>
public class PlaywrightHarnessTests
{
    [Test]
    public async Task ChromiumLaunchesAndRendersMarkup()
    {
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();
        await page.SetContentAsync("<main><h1>harness</h1></main>");

        var heading = await page.TextContentAsync("h1");
        heading.Should().Be("harness");
    }
}
