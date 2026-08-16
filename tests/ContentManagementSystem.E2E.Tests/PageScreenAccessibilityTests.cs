using System.Security.Claims;

using ContentManagementSystem.Client.Components.Admin.Pages;
using ContentManagementSystem.Client.Components.Admin.Reusable;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The accessibility gate over the page admin screens (task P2-23).
/// </summary>
/// <remarks>
/// The same shape as <see cref="StructureScreenAccessibilityTests"/>, and that file's remarks explain
/// why these render components rather than driving the running site, and what the full-journey pass
/// in Phase 9 is still left to cover.
/// <para>
/// One thing is specific to these screens. The page editor's controls are built from a template
/// revision's captured zones, and the label-for-control pairing is generated per zone key — which is
/// exactly the kind of thing that works for the zone the developer had on screen and breaks for the
/// next one. The fixture therefore renders three zones of three different field types, including one
/// the plain editor can only show read-only.
/// </para>
/// </remarks>
public class PageScreenAccessibilityTests
{
    /// <summary>The rule sets the gate enforces. See the structure suite for why best-practice is in.</summary>
    private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"];

    /// <summary>
    /// The screens under the gate, with a string each must have rendered.
    /// </summary>
    /// <remarks>
    /// The expected string is the load-bearing part: these screens fetch in <c>OnParametersSetAsync</c>,
    /// so a renderer that did not wait would hand axe a page reading "Loading page…" — no tables, no
    /// forms, and no violations, with the gate going green having checked nothing.
    /// </remarks>
    public static TheoryData<string, Type, Dictionary<string, object?>, string> Screens => new()
    {
        { "page list", typeof(PageList), [], "Enterprise" },
        {
            "page editor",
            typeof(PageEditor),
            new() { ["Id"] = FakePageClient.Id },
            "What our plans cost"
        },
        {
            "version history",
            typeof(PageVersions),
            new() { ["Id"] = FakePageClient.Id },
            "before the big rewrite"
        },
        {
            "preview links",
            typeof(PagePreviewLinks),
            new() { ["Id"] = FakePageClient.Id },
            "Sent to the agency"
        },
        { "reusable content library", typeof(ReusableLibrary), [], "Spring banner" },
        {
            "reusable content editor",
            typeof(ReusableEditor),
            new() { ["Id"] = FakeReusableClient.Id },
            // The where-used panel, which is the part of this screen with the most for axe to judge:
            // a table of affected pages, three badge states, and a nested list of items.
            "Enterprise"
        },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task APageScreenHasNoAccessibilityViolations(
        string description,
        Type component,
        Dictionary<string, object?> parameters,
        string expected)
    {
        var html = await RenderAsync(component, parameters);

        html.Should().Contain(
            expected,
            "the gate must inspect a loaded screen, not the placeholder shown while it fetches");

        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        await page.SetContentAsync(Document(description, html));

        var results = await page.RunAxe(new AxeRunOptions
        {
            RunOnly = new RunOnlyOptions { Type = "tag", Values = [.. Tags] },
        });

        var failures = results.Violations
            .Select(violation =>
                $"{violation.Id} ({violation.Impact}): {violation.Help} — " +
                string.Join("; ", violation.Nodes.Select(node => string.Join(",", node.Target))))
            .ToList();

        failures.Should().BeEmpty(
            "the {0} screen must be usable without a mouse or a screen. axe reported: {1}",
            description,
            string.Join(" | ", failures));

        results.Passes.Should().NotBeEmpty("axe must actually have run against rendered markup");
    }

    [Fact]
    public async Task TheDiffViewerDistinguishesAMovedBlockWithoutRelyingOnColour()
    {
        var html = await RenderAsync(
            typeof(PageVersions),
            new Dictionary<string, object?> { ["Id"] = FakePageClient.Id });

        // The history screen preselects published-against-draft but does not run the comparison
        // until asked, so the diff itself is rendered directly.
        var diff = await new FakePageClient().GetDiffAsync(
            FakePageClient.Id,
            FakePageClient.PublishedVersionId,
            FakePageClient.DraftVersionId,
            TestContext.Current.CancellationToken);

        var rendered = await RenderAsync(
            typeof(ContentDiffView),
            new Dictionary<string, object?> { ["Diff"] = diff });

        html.Should().Contain("Compare selected versions");

        // The distinction the diff exists to draw has to survive into the markup as a word, not only
        // as a colour: "Moved" is what a screen reader announces and what a monochrome print shows.
        rendered.Should().Contain("Moved").And.Contain("position 1 → 2");
        rendered.Should().Contain("<ins").And.Contain("<del");
    }

    /// <summary>
    /// Renders one component to HTML with the framework's static renderer.
    /// </summary>
    /// <remarks>
    /// Signed in as an Administrator, which is deliberate: these screens hide the save, publish, and
    /// restore controls behind <c>AuthorizeView</c>, and a gate run as a Viewer would inspect a page
    /// with no buttons on it and find nothing to complain about.
    /// </remarks>
    private static async Task<string> RenderAsync(Type component, Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IPageClient, FakePageClient>();
        services.AddScoped<IReusableClient, FakeReusableClient>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, AdministratorStateProvider>();

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new PrerenderingHtmlRenderer(
            provider,
            provider.GetRequiredService<ILoggerFactory>());

        return await renderer.RenderAsync(component, ParameterView.FromDictionary(parameters));
    }

    /// <summary>Wraps a screen's markup in the smallest document axe can judge fairly.</summary>
    private static string Document(string description, string html) =>
        $"""
         <!doctype html>
         <html lang="en">
         <head><meta charset="utf-8"><title>{description}</title></head>
         <body><main>{html}</main></body>
         </html>
         """;

    /// <summary>Signs the render in as an Administrator, so every gated control is on the page.</summary>
    private sealed class AdministratorStateProvider : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, "test-editor"),
                    new Claim(ClaimTypes.Role, CmsRoles.Administrator),
                ],
                authenticationType: "Test",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
