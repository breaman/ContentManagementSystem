using System.IO.Compression;
using Microsoft.Playwright;
using S3.EditorInterop.Server;

// ---------------------------------------------------------------------------------------------
// S3 — Editor JS interop in Blazor WebAssembly.
//
// Question: do CodeMirror 6 and Quill integrate cleanly (init, two-way bind, dispose without leaks)
// as local assets under a strict CSP?
// Throwaway code — see docs/spikes/s3-editor-interop.md.
// ---------------------------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:0");

// The Blazor client's `_framework` assets reach the server through the static web assets manifest,
// which the host only reads automatically in Development. The spike runs in Release, so ask for it.
builder.WebHost.UseStaticWebAssets();

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

var indexHtml = await File.ReadAllTextAsync(
    Path.Combine(app.Environment.WebRootPath, "index.html"));

// The host page, served with a per-request nonce that the JS side reads out of the meta tag.
app.MapGet("/", (HttpContext http) =>
{
    var nonce = Csp.NewNonce();
    http.Response.Headers.ContentSecurityPolicy = Csp.WithNonce(nonce);

    return Results.Content(indexHtml.Replace("__CSP_NONCE__", nonce, StringComparison.Ordinal), "text/html");
});

// The control: same page, same strict policy, but no nonce for the editors to use.
app.MapGet("/no-nonce", (HttpContext http) =>
{
    http.Response.Headers.ContentSecurityPolicy = Csp.WithoutNonce();

    var html = indexHtml.Replace(
        """<meta name="csp-nonce" content="__CSP_NONCE__" />""",
        string.Empty,
        StringComparison.Ordinal);

    return Results.Content(html, "text/html");
});

// `--serve` runs the host on its own, for poking at the page by hand.
if (args.Contains("--serve"))
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:5599");
    await app.RunAsync();

    return 0;
}

await app.StartAsync();

var address = app.Urls.First();

Console.WriteLine("S3 — editor JS interop in Blazor WebAssembly");
Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  ·  .NET {Environment.Version}  ·  {address}");
Console.WriteLine("CodeMirror 6 and Quill bundled locally with esbuild; no CDN.");

Console.WriteLine();
Console.WriteLine("Installing the Playwright browser if needed…");
Microsoft.Playwright.Program.Main(["install", "chromium", "--with-deps"]);

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

// ---------------------------------------------------------------------------------------------
Check.Section("1. Both editors initialize under the strict policy");

var page = await browser.NewPageAsync();
var consoleErrors = new List<string>();
page.Console += (_, message) =>
{
    if (message.Type == "error")
    {
        consoleErrors.Add(message.Text);
    }
};

await page.GotoAsync(address);
await page.Locator("#ready").WaitForAsync(new LocatorWaitForOptions { Timeout = 120_000 });

Check.Note($"Blazor WebAssembly booted with script-src 'self' 'wasm-unsafe-eval' (no unsafe-inline, no unsafe-eval).");

await page.Locator(".cm-content").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });
await page.Locator(".ql-editor").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

Check.That(await page.Locator(".cm-editor").CountAsync() == 1, "CodeMirror 6 mounted");
Check.That(await page.Locator(".ql-editor").CountAsync() == 1, "Quill mounted");
Check.That(await page.Locator(".ql-toolbar").CountAsync() == 1, "Quill built its toolbar");

Check.That(
    (await page.Locator(".cm-content").TextContentAsync())?.Contains("Why teams choose us", StringComparison.Ordinal) == true,
    "the editor received its initial value from .NET");

Check.That(
    (await page.Locator(".ql-editor").TextContentAsync())?.Contains("Editors stopped filing tickets", StringComparison.Ordinal) == true,
    "Quill received its initial HTML from .NET");

// CodeMirror injects its theme as a <style> element at runtime. If the nonce did not reach it, the
// style is blocked and this computed value falls back to the browser default.
var whiteSpace = await page.EvaluateAsync<string>(
    "() => getComputedStyle(document.querySelector('.cm-content')).whiteSpace");

// A bare <div> computes white-space: normal. CodeMirror's own stylesheet sets it to pre, so any
// value other than normal means the injected <style> survived the policy.
Check.That(whiteSpace != "normal",
    "CodeMirror's injected stylesheet was applied — its nonce satisfied style-src",
    $"computed white-space on .cm-content: {whiteSpace} (unstyled would be 'normal')");

// ---------------------------------------------------------------------------------------------
Check.Section("2. Two-way binding");

await page.Locator(".cm-content").ClickAsync();
await page.Keyboard.PressAsync("End");
await page.Keyboard.TypeAsync(" Typed in CodeMirror.");

await page.Locator("#markdown-value")
    .Filter(new LocatorFilterOptions { HasTextString = "Typed in CodeMirror." })
    .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

Check.That(true, "editor → .NET: typing in CodeMirror updates the bound .NET value");

await page.Locator(".ql-editor").ClickAsync();
await page.Keyboard.PressAsync("End");
await page.Keyboard.TypeAsync(" Typed in Quill.");

await page.Locator("#richtext-value")
    .Filter(new LocatorFilterOptions { HasTextString = "Typed in Quill." })
    .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

Check.That(true, "editor → .NET: typing in Quill updates the bound .NET value");

await page.Locator("#set-from-dotnet").ClickAsync();

await page.Locator(".cm-content")
    .Filter(new LocatorFilterOptions { HasTextString = "Set from .NET" })
    .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });
await page.Locator(".ql-editor")
    .Filter(new LocatorFilterOptions { HasTextString = "Set from .NET" })
    .WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

Check.That(true, ".NET → editor: a programmatic write reaches both editors");

var changeEventsAfterProgrammaticWrite = await page.EvaluateAsync<int>("() => window.__cmsEditors.stats.changeEvents");
await page.Locator("#set-from-dotnet").ClickAsync();
await Task.Delay(250);
var changeEventsAfterSecondWrite = await page.EvaluateAsync<int>("() => window.__cmsEditors.stats.changeEvents");

Check.That(
    changeEventsAfterSecondWrite == changeEventsAfterProgrammaticWrite,
    "writing the same value again produces no change event — the binding does not echo",
    $"change events: {changeEventsAfterProgrammaticWrite} → {changeEventsAfterSecondWrite}");

// ---------------------------------------------------------------------------------------------
Check.Section("3. Disposal and leaks");

var styleTagsAfterFirstMount = await page.EvaluateAsync<int>("() => window.__cmsEditors.domCounts().styleTags");

await page.Locator("#toggle-editors").ClickAsync();
await page.Locator(".cm-editor").WaitForAsync(new LocatorWaitForOptions
{
    State = WaitForSelectorState.Detached,
    Timeout = 10_000,
});

var afterUnmount = await DomCountsAsync(page);
var liveAfterUnmount = await page.EvaluateAsync<int>("() => window.__cmsEditors.live()");

Check.That(liveAfterUnmount == 0, "the JS-side registry is empty after unmounting");
Check.That(afterUnmount.CodeMirror == 0, "no CodeMirror DOM survives disposal");
Check.That(afterUnmount.QuillEditor == 0 && afterUnmount.QuillToolbar == 0,
    "no Quill DOM survives disposal — including the toolbar it appended as a sibling",
    $"cm={afterUnmount.CodeMirror} ql-editor={afterUnmount.QuillEditor} ql-toolbar={afterUnmount.QuillToolbar}");

for (var cycle = 0; cycle < 10; cycle++)
{
    await page.Locator("#toggle-editors").ClickAsync();
    await page.Locator(".cm-content").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
    await page.Locator("#toggle-editors").ClickAsync();
    await page.Locator(".cm-editor").WaitForAsync(new LocatorWaitForOptions
    {
        State = WaitForSelectorState.Detached,
        Timeout = 15_000,
    });
}

var created = await page.EvaluateAsync<int>("() => window.__cmsEditors.stats.created");
var disposed = await page.EvaluateAsync<int>("() => window.__cmsEditors.stats.disposed");
var finalCounts = await DomCountsAsync(page);
var finalLive = await page.EvaluateAsync<int>("() => window.__cmsEditors.live()");

Check.That(created == disposed && created >= 22,
    "after 11 mount/unmount cycles every editor created was disposed",
    $"created={created} disposed={disposed}");
Check.That(finalLive == 0, "the registry is still empty");
Check.That(finalCounts.CodeMirror == 0 && finalCounts.QuillEditor == 0 && finalCounts.QuillToolbar == 0,
    "no editor DOM accumulated across cycles");
Check.That(finalCounts.StyleTags <= styleTagsAfterFirstMount,
    "injected <style> elements do not accumulate across cycles",
    $"style tags: {styleTagsAfterFirstMount} after the first mount → {finalCounts.StyleTags} after eleven");

Check.That(consoleErrors.Count == 0,
    "no console errors across the whole session",
    consoleErrors.Count == 0 ? "clean" : string.Join(" | ", consoleErrors.Take(5)));

// ---------------------------------------------------------------------------------------------
Check.Section("4. What the strict CSP actually blocked");

var violations = await ViolationsAsync(page);

Check.That(violations.Count == 0,
    "no CSP violations with a nonce in play",
    violations.Count == 0 ? "clean" : string.Join(" | ", violations));

// The Quill link tooltip positions itself with an inline style attribute. Nonces do not apply to
// style attributes, so this is the case that decides whether style-src-attr must be relaxed.
await page.Locator("#toggle-editors").ClickAsync();
await page.Locator(".ql-editor").WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });
await page.Locator(".ql-editor").ClickAsync();
await page.Keyboard.PressAsync("Control+A");
await page.Locator("button.ql-link").ClickAsync();
await Task.Delay(500);

var afterTooltip = await ViolationsAsync(page);
var attributeViolations = afterTooltip.Where(v => v.Contains("style-src-attr", StringComparison.Ordinal)).ToList();
var tooltipVisible = await page.Locator(".ql-tooltip").CountAsync() > 0;
var tooltipStyle = tooltipVisible
    ? await page.Locator(".ql-tooltip").First.GetAttributeAsync("style") ?? "(none)"
    : "(no tooltip element)";

Check.That(tooltipVisible, "Quill's link tooltip opened, so the inline-style path was actually exercised");
Check.That(attributeViolations.Count == 0,
    "Quill positions its tooltip without tripping style-src-attr",
    $"tooltip style attribute: {tooltipStyle}");

// ---------------------------------------------------------------------------------------------
Check.Section("5. The control: the same page with no nonce");

var controlPage = await browser.NewPageAsync();
await controlPage.GotoAsync($"{address.TrimEnd('/')}/no-nonce");
await controlPage.Locator("#ready").WaitForAsync(new LocatorWaitForOptions { Timeout = 120_000 });
await controlPage.Locator(".cm-content").WaitForAsync(new LocatorWaitForOptions { Timeout = 30_000 });

var controlWhiteSpace = await controlPage.EvaluateAsync<string>(
    "() => getComputedStyle(document.querySelector('.cm-content')).whiteSpace");
var controlViolations = await ViolationsAsync(controlPage);

Check.That(controlViolations.Count > 0,
    "without a nonce, the strict policy blocks the editors' injected stylesheets",
    controlViolations.Count == 0
        ? "no violations — the nonce may not be load-bearing after all"
        : string.Join(" | ", controlViolations.Distinct().Take(3)));

Check.That(controlWhiteSpace != whiteSpace,
    "and the editor renders unstyled as a result",
    $"with nonce: white-space={whiteSpace} · without nonce: white-space={controlWhiteSpace}");

// ---------------------------------------------------------------------------------------------
Check.Section("6. Asset weight");

var bundle = Path.Combine(app.Environment.WebRootPath, "js", "editors.js");
var raw = await File.ReadAllBytesAsync(bundle);

using var compressed = new MemoryStream();
await using (var gzip = new GZipStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
{
    await gzip.WriteAsync(raw);
}

var quillCss = new FileInfo(Path.Combine(app.Environment.WebRootPath, "css", "quill.snow.css")).Length;

Check.Note($"editors.js (CodeMirror 6 + Quill + markdown/html languages, minified): {raw.Length / 1024.0:N0} KB raw, {compressed.Length / 1024.0:N0} KB gzipped");
Check.Note($"quill.snow.css: {quillCss / 1024.0:N0} KB raw");
Check.Note("Backoffice-only: none of this is served to an anonymous visitor of a public page.");

await app.StopAsync();

return Check.Summarize();

// ---------------------------------------------------------------------------------------------

static async Task<List<string>> ViolationsAsync(IPage page) =>
    [.. await page.EvaluateAsync<string[]>(
        "() => (window.__cspViolations || []).map(v => v.directive + ' ← ' + (v.sample || v.blockedUri))")];

// Playwright's evaluator needs a parameterless constructor, so the counts are read one at a time
// rather than deserialized into a record.
static async Task<DomCounts> DomCountsAsync(IPage page) => new(
    await page.EvaluateAsync<int>("() => window.__cmsEditors.domCounts().codeMirror"),
    await page.EvaluateAsync<int>("() => window.__cmsEditors.domCounts().quillEditor"),
    await page.EvaluateAsync<int>("() => window.__cmsEditors.domCounts().quillToolbar"),
    await page.EvaluateAsync<int>("() => window.__cmsEditors.domCounts().styleTags"));

internal sealed record DomCounts(int CodeMirror, int QuillEditor, int QuillToolbar, int StyleTags);
