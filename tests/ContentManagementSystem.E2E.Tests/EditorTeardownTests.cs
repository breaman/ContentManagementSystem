using System.Text.Json;

using Microsoft.Playwright;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Mounting and unmounting the JavaScript editors, in a real browser (task P6-31a, risk R14).
/// </summary>
/// <remarks>
/// The two failures this exists for are both silent. An editor that is never torn down leaves its
/// DOM, its listeners, and its registry entry behind, and a backoffice session that opens forty
/// pages ends up with forty CodeMirrors it cannot see; and a CodeMirror rendered without the style
/// nonce works perfectly and looks like a plain textarea, with no exception and nothing in the
/// console ([S3](../../docs/spikes/s3-editor-interop.md), ADR-0013).
/// <para>
/// <strong>What this covers, and what it does not.</strong> It drives the bundles directly, so it
/// asserts on the half that only a browser can judge: the libraries' own teardown, Quill's toolbar
/// sibling, and whether the injected stylesheet was allowed. The .NET half —
/// <c>JsEditorComponentBase</c> releasing its <c>DotNetObjectReference</c> — is asserted by the
/// component suite, because CodeMirror and Quill never mount under bUnit at all.
/// </para>
/// </remarks>
public class EditorTeardownTests
{
    /// <summary>How many times each editor is mounted and unmounted.</summary>
    /// <remarks>
    /// Ten, as the task names. The number matters less than that it is more than one: a leak of one
    /// node per mount is invisible at a single mount and obvious at ten.
    /// </remarks>
    private const int Cycles = 10;

    [Fact]
    public async Task MountingAndUnmountingASourceEditorTenTimesLeavesNothingBehind()
    {
        var counts = await CycleAsync("cms-source-editor.js", """
            module.create(id, host, "# Heading", dotNet, "markdown", "Body");
            """);

        counts.Created.Should().Be(Cycles);
        counts.Disposed.Should().Be(
            Cycles,
            "a registry entry that survives its editor keeps the whole component alive (risk R14)");
        counts.Live.Should().Be(0);
        counts.CodeMirror.Should().Be(
            0,
            "CodeMirror's destroy() takes its own DOM with it, and a node left behind means it was " +
            "never called");
    }

    [Fact]
    public async Task MountingAndUnmountingAWysiwygEditorTenTimesLeavesNoToolbarsBehind()
    {
        var counts = await CycleAsync("cms-wysiwyg-editor.js", """
            module.create(id, host, "<p>Hello</p>", dotNet, false, "Body");
            """);

        counts.Created.Should().Be(Cycles);
        counts.Disposed.Should().Be(Cycles);
        counts.Live.Should().Be(0);

        // The finding S3 spent its time on. Quill has no destroy() and appends its toolbar as a
        // SIBLING of the container, so a teardown that only clears the container accumulates one
        // toolbar per mount — ten stale toolbars in a session that opened ten pages.
        counts.QuillToolbar.Should().Be(
            0,
            "Quill's toolbar is a sibling of the container, so clearing the container does not remove it");
        counts.QuillEditor.Should().Be(0);
    }

    [Fact]
    public async Task CodeMirrorIsStyledUnderAStrictPolicyBecauseTheNonceReachesIt()
    {
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        var refusals = new List<string>();

        // A refused stylesheet is reported as a console error and nothing else — no exception, and
        // an editor that still works. Collecting them is what turns a silent failure into a message.
        page.Console += (_, message) =>
        {
            if (message.Type == "error") refusals.Add(message.Text);
        };

        await EditorAssets.ServeAsync(page, Host);
        await page.GotoAsync($"{EditorAssets.Origin}/");

        await page.EvaluateAsync(
            """
            async () => {
                const module = await import("/cms-source-editor.js");
                const host = document.getElementById("host");

                module.create("only", host, "# Heading", { invokeMethodAsync: async () => {} },
                    "markdown", "Body");
            }
            """);

        await page.WaitForSelectorAsync(".cm-editor");

        // CodeMirror's theme is injected at runtime as a <style> element. Under the strict policy it
        // is served with, that element is only honoured if EditorView.cspNonce carried this
        // request's nonce — so a display of "flex" is the nonce arriving, and the browser default of
        // "block" is D13's silent failure.
        var display = await page.EvalOnSelectorAsync<string>(
            ".cm-editor",
            "element => getComputedStyle(element).display");

        display.Should().Be(
            "flex",
            "an unstyled CodeMirror looks like a plain textarea and reports nothing at all");

        refusals.Should().BeEmpty("the policy refused something the editor needed: {0}",
            string.Join(" | ", refusals));
    }

    [Fact]
    public async Task WithoutTheNonceTheSameEditorIsSilentlyUnstyled()
    {
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        // The policy is on, the bundle is fine, and nobody has wired the meta tag — which is the
        // deployment mistake being reproduced rather than an invented one.
        await EditorAssets.ServeAsync(page, Host, strictStyles: true, withNonceMeta: false);

        await page.GotoAsync($"{EditorAssets.Origin}/");

        await page.EvaluateAsync(
            """
            async () => {
                const module = await import("/cms-source-editor.js");
                const host = document.getElementById("host");

                module.create("only", host, "# Heading", { invokeMethodAsync: async () => {} },
                    "markdown", "Body");
            }
            """);

        await page.WaitForSelectorAsync(".cm-editor");

        var display = await page.EvalOnSelectorAsync<string>(
            ".cm-editor",
            "element => getComputedStyle(element).display");

        // The whole reason the test above is worth having: nothing throws, the editor still accepts
        // typing, and the only observable difference is that it is not styled. A suite that did not
        // assert the positive case would go green on a build with no nonce at all.
        display.Should().Be(
            "block",
            "this is what D13's silent failure looks like, and it is why the positive case is asserted");
    }

    /// <summary>Mounts and unmounts one bundle's editor repeatedly and reads the registry.</summary>
    /// <param name="bundle">File name of the bundle under test.</param>
    /// <param name="create">The <c>create</c> call, given <c>module</c>, <c>host</c>, <c>id</c>, <c>dotNet</c>.</param>
    private static async Task<EditorCounts> CycleAsync(string bundle, string create)
    {
        PlaywrightBrowsers.EnsureInstalled();

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync();

        var page = await browser.NewPageAsync();

        await EditorAssets.ServeAsync(page, Host);
        await page.GotoAsync($"{EditorAssets.Origin}/");

        var json = await page.EvaluateAsync<string>(
            $$"""
            async () => {
                const module = await import("/{{bundle}}");
                const host = document.getElementById("host");

                // A DotNetObjectReference as the bundle sees one: the only thing it does with it is
                // report changes, and nothing here changes anything.
                const dotNet = { invokeMethodAsync: async () => {} };

                for (let i = 0; i < {{Cycles}}; i++) {
                    const id = `editor-${i}`;

                    {{create}}

                    module.dispose(id);
                }

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

    /// <summary>The one element every mount is given.</summary>
    /// <remarks>
    /// Reused across cycles on purpose. A teardown that leaves nodes behind in a container that is
    /// itself replaced would look clean; the container an editor is mounted into in the backoffice
    /// outlives the editor, because Blazor owns it.
    /// </remarks>
    private const string Host = """<div id="host"></div>""";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>What the registry and the document report after a run of cycles.</summary>
    private sealed record EditorCounts(
        int Created,
        int Disposed,
        int Live,
        int CodeMirror,
        int QuillEditor,
        int QuillToolbar);
}
