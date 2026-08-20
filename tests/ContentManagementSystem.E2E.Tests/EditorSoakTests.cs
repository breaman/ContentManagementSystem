using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// The long editing session risk <c>R14</c> is stated against (task P9-16).
/// </summary>
/// <remarks>
/// <c>EditorTeardownTests</c> proves the instrument reads zero on a short run: ten mount/unmount
/// cycles, created equal to disposed, no surviving node. What it cannot answer is the trigger, which
/// is browser memory over hours — a leak of a few kilobytes a cycle is invisible in ten cycles and
/// is the whole of R14.
/// <para>
/// So this test cycles editors for a duration and compares the heap at the end against the heap
/// after a warm-up, with a forced collection before each sample. Sixty seconds by default, so the
/// harness runs on every build and a gross regression fails there; <strong>the eight-hour run the
/// task names is <c>CMS_SOAK_MINUTES=480</c></strong>, and until somebody has run it R14 stays open
/// however green this is.
/// </para>
/// <para>
/// The warm-up matters more than it looks. The first cycles allocate everything that is allocated
/// once — the modules, CodeMirror's and Quill's own singletons, the registry — and measuring from
/// zero would report that as growth and fail a run that leaked nothing at all.
/// </para>
/// </remarks>
public class EditorSoakTests
{
    /// <summary>How long to cycle for, in minutes. Set it to 480 for the run the task names.</summary>
    public const string DurationVariable = "CMS_SOAK_MINUTES";

    /// <summary>R14's trigger: fail if the heap grows by more than half.</summary>
    private const double GrowthCeiling = 1.5;

    /// <summary>Editors mounted and disposed between samples.</summary>
    private const int CyclesPerBatch = 20;

    private const string Host = """<div id="host"></div>""";

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    [Test]
    public async Task CyclingTheEditorsDoesNotGrowTheHeap()
    {
        PlaywrightBrowsers.EnsureInstalled();

        var duration = Duration();

        using var playwright = await Playwright.CreateAsync();

        // Two flags, both load-bearing. Without --expose-gc a sample includes whatever the collector
        // had not got to yet, which is noise of the same order as the thing being measured; without
        // --enable-precise-memory-info Chrome rounds usedJSHeapSize hard enough to hide a slow leak.
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Args = ["--js-flags=--expose-gc", "--enable-precise-memory-info"],
        });

        var page = await browser.NewPageAsync();

        await EditorAssets.ServeAsync(page, Host);
        await page.GotoAsync($"{EditorAssets.Origin}/");

        await CycleAsync(page, CyclesPerBatch);

        var baseline = await HeapAsync(page);

        baseline.Should().BeGreaterThan(0, "the browser must report a heap size for this to mean anything");

        var samples = new List<long>();
        var started = Stopwatch.GetTimestamp();
        var batches = 0;

        while (Stopwatch.GetElapsedTime(started) < duration)
        {
            await CycleAsync(page, CyclesPerBatch);

            batches++;
            samples.Add(await HeapAsync(page));
        }

        var counts = await CountsAsync(page);
        var final = samples[^1];
        var growth = (double)final / baseline;

        await TestContext.Current!.OutputWriter.WriteLineAsync(
            $"soak over {duration.TotalMinutes:F0} min: {batches * CyclesPerBatch * 2} editors mounted " +
            $"and disposed, heap {baseline / 1024} KB → {final / 1024} KB " +
            $"(peak {samples.Max() / 1024} KB), growth {growth:P0}.");

        counts.Created.Should().Be(counts.Disposed, "every editor mounted was disposed");
        counts.Live.Should().Be(0);
        counts.CodeMirror.Should().Be(0);
        counts.QuillEditor.Should().Be(0);
        counts.QuillToolbar.Should().Be(0);

        growth.Should().BeLessThan(
            GrowthCeiling,
            "R14 fails when browser memory grows more than 50%: {0} KB became {1} KB over {2} cycles",
            baseline / 1024,
            final / 1024,
            batches * CyclesPerBatch * 2);
    }

    /// <summary>Mounts and disposes both editors, <paramref name="cycles"/> times each.</summary>
    private static async Task CycleAsync(IPage page, int cycles) =>
        await page.EvaluateAsync(
            $$"""
            async () => {
                const source = await import("/cms-source-editor.js");
                const wysiwyg = await import("/cms-wysiwyg-editor.js");
                const host = document.getElementById("host");
                const dotNet = { invokeMethodAsync: async () => {} };

                for (let i = 0; i < {{cycles}}; i++) {
                    const markdown = `soak-source-${i}`;
                    source.create(markdown, host, "# Heading", dotNet, "markdown", "Body");
                    source.dispose(markdown);

                    const rich = `soak-wysiwyg-${i}`;
                    wysiwyg.create(rich, host, "<p>Hello</p>", dotNet, false, "Body");
                    wysiwyg.dispose(rich);
                }
            }
            """);

    /// <summary>Collects, then reads the heap.</summary>
    /// <remarks>
    /// Collection is asked for twice with a turn of the event loop between: the first pass frees
    /// what is unreachable, and objects that were only reachable from it become collectable in the
    /// second. One pass under-reports the effect of a teardown that dropped a whole graph.
    /// </remarks>
    private static async Task<long> HeapAsync(IPage page) =>
        await page.EvaluateAsync<long>(
            """
            async () => {
                if (globalThis.gc) {
                    globalThis.gc();
                    await new Promise(resolve => setTimeout(resolve, 50));
                    globalThis.gc();
                }

                return performance.memory ? performance.memory.usedJSHeapSize : 0;
            }
            """);

    private static async Task<EditorCounts> CountsAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string>(
            """
            () => {
                const registry = globalThis.__cmsEditors;
                const dom = registry.domCounts();

                return JSON.stringify({
                    created: registry.stats.created,
                    disposed: registry.stats.disposed,
                    live: registry.live(),
                    codeMirror: dom.codeMirror,
                    quillEditor: dom.quillEditor,
                    quillToolbar: dom.quillToolbar,
                });
            }
            """);

        return JsonSerializer.Deserialize<EditorCounts>(json, JsonOptions)!;
    }

    /// <summary>How long to cycle for, from the environment or the default minute.</summary>
    private static TimeSpan Duration() =>
        double.TryParse(
            Environment.GetEnvironmentVariable(DurationVariable),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes)
            : TimeSpan.FromSeconds(60);

    private sealed record EditorCounts(
        int Created,
        int Disposed,
        int Live,
        int CodeMirror,
        int QuillEditor,
        int QuillToolbar);
}
