using ContentManagementSystem.Client.Components.Admin.Structure;
using ContentManagementSystem.Shared.Services;

using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The continuous accessibility gate, starting at the structure screens (task P1-31).
/// </summary>
/// <remarks>
/// <b>Why this renders components instead of driving the running site.</b> The gate has to run on
/// every push to be a gate at all, and a browser journey through the real application needs a
/// database, a migrated schema, a seeded user, and a login before it can reach <c>/admin</c> — which
/// is a nightly job, not a pull-request check. What axe actually inspects is the DOM, and the DOM is
/// a property of the components; rendering them with the framework's own static renderer and handing
/// the result to a real browser tests the same markup, in about a second.
/// <para>
/// <b>What that leaves for Phase 9.</b> Rules that depend on the compiled stylesheet — colour
/// contrast above all — cannot fire here, because no CSS is loaded. Nor can anything about focus
/// order across a real navigation. Those belong to the full-journey axe pass in <c>P9</c>; this gate
/// covers the structural rules, which are the ones a developer breaks while writing a screen.
/// </para>
/// </remarks>
public class StructureScreenAccessibilityTests
{
    /// <summary>
    /// The rule sets the gate enforces.
    /// </summary>
    /// <remarks>
    /// WCAG 2.1 AA plus axe's own best-practice pack. The best-practice rules are included
    /// deliberately even though they are not required for conformance: they cover heading order and
    /// landmark structure, which is exactly what goes wrong on an admin screen assembled from
    /// tables and forms.
    /// </remarks>
    private static readonly string[] Tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"];

    /// <summary>
    /// The screens under the gate, with a string each must have rendered.
    /// </summary>
    /// <remarks>
    /// The expected string is the load-bearing part. These screens fetch their content in
    /// <c>OnInitializedAsync</c>, so a renderer that did not wait would hand axe a page reading
    /// "Loading templates…" — which has no tables, no forms, and no violations. The gate would go
    /// green having checked nothing at all.
    /// </remarks>
    public static TheoryData<string, Type, Dictionary<string, object?>, string> Screens => new()
    {
        { "template list", typeof(Templates), [], "marketing-landing" },
        {
            "template editor",
            typeof(TemplateEditor),
            new() { ["Id"] = FakeStructureClient.Id },
            "heroTitle"
        },
        { "block type list", typeof(BlockTypes), [], "hero-banner" },
        {
            "block type editor",
            typeof(BlockTypeEditor),
            new() { ["Id"] = FakeStructureClient.Id },
            "spacing-options"
        },
    };

    [Theory]
    [MemberData(nameof(Screens))]
    public async Task AStructureScreenHasNoAccessibilityViolations(
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

        // A run that inspected nothing would pass silently, which is the failure mode of every
        // accessibility gate that has ever been quietly disabled.
        results.Passes.Should().NotBeEmpty("axe must actually have run against rendered markup");
    }

    /// <summary>
    /// Renders one component to HTML with the framework's static renderer.
    /// </summary>
    /// <param name="component">The component to render.</param>
    /// <param name="parameters">Its route parameters.</param>
    /// <returns>The markup the screen produces.</returns>
    private static async Task<string> RenderAsync(Type component, Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IStructureClient, FakeStructureClient>();

        // The sub-navigation between the two structure screens is built from <NavLink>, which
        // resolves a NavigationManager when it is constructed in order to work out whether it is the
        // active link. A static render has nowhere to navigate to, so the substitute records rather
        // than moves — the same one the other backoffice gates use.
        services.AddScoped<NavigationManager, StaticNavigationManager>();

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new PrerenderingHtmlRenderer(
            provider,
            provider.GetRequiredService<ILoggerFactory>());

        return await renderer.RenderAsync(component, ParameterView.FromDictionary(parameters));
    }

    /// <summary>
    /// Wraps a screen's markup in the smallest document axe can judge fairly.
    /// </summary>
    /// <remarks>
    /// The language, title, and <c>main</c> landmark come from the application's layout rather than
    /// from the screens, so they are supplied here. Leaving them out would make every screen fail on
    /// three rules it does not own, and the usual response to that is to disable the rules.
    /// </remarks>
    private static string Document(string description, string html) =>
        $"""
         <!doctype html>
         <html lang="en">
         <head><meta charset="utf-8"><title>{description}</title></head>
         <body><main>{html}</main></body>
         </html>
         """;
}
