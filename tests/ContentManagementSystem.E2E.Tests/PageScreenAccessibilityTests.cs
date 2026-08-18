using System.Security.Claims;

using ContentManagementSystem.Client.Components.Admin.Dashboard;
using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Client.Components.Admin.Media;
using ContentManagementSystem.Client.Components.Admin.Pages;
using ContentManagementSystem.Client.Components.Admin.RecycleBin;
using ContentManagementSystem.Client.Components.Admin.Reusable;
using ContentManagementSystem.Client.Components.Admin.Tree;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
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
    public static IEnumerable<(string Description, Type Component,
        Dictionary<string, object?> Parameters, string Expected)> Screens =>
    [
        ("page list", typeof(PageList), [], "Enterprise"),
        (
            "page editor",
            typeof(PageEditor),
            new() { ["Id"] = FakePageClient.Id },
            "What our plans cost"
        ),
        (
            "version history",
            typeof(PageVersions),
            new() { ["Id"] = FakePageClient.Id },
            "before the big rewrite"
        ),
        (
            "preview links",
            typeof(PagePreviewLinks),
            new() { ["Id"] = FakePageClient.Id },
            "Sent to the agency"
        ),
        ("reusable content library", typeof(ReusableLibrary), [], "Spring banner"),
        (
            "reusable content editor",
            typeof(ReusableEditor),
            new() { ["Id"] = FakeReusableClient.Id },
            // The where-used panel, which is the part of this screen with the most for axe to judge:
            // a table of affected pages, three badge states, and a nested list of items.
            "Enterprise"
        ),

        // The media screens are the ones where an accessibility gate has the most to say, because
        // they are the ones full of images: every tile in the grid carries an alt attribute whose
        // value comes from editorial data, and the fixture deliberately includes an item nobody has
        // described (tasks P5-19 and P5-22).
        ("media library", typeof(MediaLibrary), [], "team-photo.jpg"),
        (
            "media item",
            typeof(MediaItemEditor),
            new() { ["Id"] = FakeMediaClient.PlacedId },
            "Team photograph"
        ),

        // The screens Phase 6 added (tasks P6-24 to P6-28). The dashboard is the one with the most
        // for axe to judge — four cards of nested lists, each row a link with a second line — and the
        // recycle bin is the one where getting it wrong matters most, since its buttons destroy
        // things and are told apart only by the page name inside them.
        ("dashboard", typeof(DashboardScreen), [], "Needs attention"),
        (
            "dashboard tile",
            typeof(DashboardTileScreen),
            new() { ["Tile"] = "NeedsAttention" },
            "Past its review date"
        ),
        ("recycle bin", typeof(RecycleBinScreen), [], "Autumn campaign"),
    ];

    [Test]
    [MethodDataSource(nameof(Screens))]
    public async Task APageScreenHasNoAccessibilityViolations(
        string description,
        Type component,
        Dictionary<string, object?> parameters,
        string expected)
    {
        var html = await BackofficeScreens.RenderAsync(component, parameters);

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

    /// <summary>
    /// The content tree, which is a pane rather than a page (tasks P6-02, P6-36).
    /// </summary>
    /// <remarks>
    /// Given its own test because it is never rendered as a whole document: it is the shell's left
    /// pane, and the screen around it owns the <c>h1</c>. Running it through the theory above would
    /// report "page should contain a level-one heading" against a component that is not a page, and
    /// the honest fix for that is to audit it in the shape it is actually used in.
    /// <para>
    /// It is worth auditing separately anyway. The tree carries more ARIA than anything else in the
    /// backoffice — a roving tabindex, a <c>treeitem</c> per row, a status indicator per row — and
    /// every one of those is the kind of thing that is right for the row the developer had on screen.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheContentTreePaneHasNoAccessibilityViolations()
    {
        var html = await BackofficeScreens.RenderAsync(typeof(ContentTree), []);

        html.Should().Contain("Pricing", "the gate must inspect a loaded tree, not its placeholder");

        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        // Wrapped as the shell wraps it: a screen with a heading, and the tree beside the canvas.
        await page.SetContentAsync(Document(
            "content tree",
            $"<h1>Content</h1><div class=\"cms-shell\">{html}</div>"));

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
            "the content tree must be usable without a mouse or a screen. axe reported: {0}",
            string.Join(" | ", failures));

        results.Passes.Should().NotBeEmpty("axe must actually have run against rendered markup");
    }

    [Test]
    public async Task TheDiffViewerDistinguishesAMovedBlockWithoutRelyingOnColour()
    {
        var html = await BackofficeScreens.RenderAsync(
            typeof(PageVersions),
            new Dictionary<string, object?> { ["Id"] = FakePageClient.Id });

        // The history screen preselects published-against-draft but does not run the comparison
        // until asked, so the diff itself is rendered directly.
        var diff = await new FakePageClient().GetDiffAsync(
            FakePageClient.Id,
            FakePageClient.PublishedVersionId,
            FakePageClient.DraftVersionId,
            TestContext.Current!.Execution.CancellationToken);

        var rendered = await BackofficeScreens.RenderAsync(
            typeof(ContentDiffView),
            new Dictionary<string, object?> { ["Diff"] = diff });

        html.Should().Contain("Compare selected versions");

        // The distinction the diff exists to draw has to survive into the markup as a word, not only
        // as a colour: "Moved" is what a screen reader announces and what a monochrome print shows.
        rendered.Should().Contain("Moved").And.Contain("position 1 → 2");
        rendered.Should().Contain("<ins").And.Contain("<del");
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
}
