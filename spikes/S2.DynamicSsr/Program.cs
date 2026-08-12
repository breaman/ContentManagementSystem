using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http.HttpResults;
using S2.DynamicSsr;
using S2.DynamicSsr.Cms;
using S2.DynamicSsr.Content;
using S2.DynamicSsr.Content.Fields;
using S2.DynamicSsr.Content.Templates;

// ---------------------------------------------------------------------------------------------
// S2 — Dynamic component rendering under static SSR.
//
// Question: does DynamicComponent compose template → zone → field renderer with no interactive
// render mode, and does an error boundary isolate a failing block?
// Throwaway code — see docs/spikes/s2-dynamic-ssr.md.
// ---------------------------------------------------------------------------------------------

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:0");

// No .AddInteractiveServerComponents(), no .AddInteractiveWebAssemblyComponents(): static SSR only.
builder.Services.AddRazorComponents();

builder.Services.AddSingleton(new TemplateRegistry(Assembly.GetExecutingAssembly()));
builder.Services.AddSingleton(new BlockTypeRegistry(Assembly.GetExecutingAssembly()));
builder.Services.AddSingleton(new FieldRendererRegistry(new Dictionary<string, Type>
{
    ["plainText"] = typeof(PlainTextRenderer),
    ["richText"] = typeof(RichTextRenderer),
    ["media"] = typeof(MediaRenderer),
    ["reusable"] = typeof(ReusableRenderer),
    ["blocks"] = typeof(BlocksRenderer),
}));
builder.Services.AddSingleton<MediaRepository>();
builder.Services.AddSingleton<ReusableContentRepository>();
builder.Services.AddSingleton<RenderDiagnostics>();

var app = builder.Build();

// Approach B — render to a string first, then set headers, then write. Cache tags accumulated during
// the render are still settable because nothing has been written yet.
app.MapGet("/b/{id:long}", async (long id, HttpContext http) =>
{
    if (!SamplePages.All.TryGetValue(id, out var page))
    {
        return Results.NotFound();
    }

    var (html, tags) = await RenderToStringAsync(http.RequestServices, page);

    http.Response.Headers["X-Cms-Cache-Tags"] = string.Join(',', tags.Order());
    http.Response.Headers.ETag = $"\"{html.GetHashCode(StringComparison.Ordinal):x8}\"";
    http.Response.ContentType = "text/html; charset=utf-8";

    return Results.Content(html, "text/html");
});

// Approach A — hand the component to RazorComponentResult and let it write the response.
app.MapGet("/a/{id:long}", async (long id, HttpContext http) =>
{
    if (!SamplePages.All.TryGetValue(id, out var page))
    {
        return Results.NotFound();
    }

    using var document = page.Parse();
    var context = CreateContext(page, document);
    var templateType = ResolveTemplate(http.RequestServices, page);

    // The tag set is empty right now. It fills up during the render — which is the problem.
    http.Response.OnStarting(() =>
    {
        http.Response.Headers["X-Cms-Cache-Tags"] = string.Join(',', context.CacheTags.Order());

        return Task.CompletedTask;
    });

    var result = new RazorComponentResult(
        typeof(DeliveryHost),
        new Dictionary<string, object?>
        {
            ["Context"] = context,
            ["TemplateType"] = templateType,
        });

    await result.ExecuteAsync(http);

    return Results.Empty;
});

await app.StartAsync();

var address = app.Urls.First();
using var client = new HttpClient { BaseAddress = new Uri(address) };
var diagnostics = app.Services.GetRequiredService<RenderDiagnostics>();

Console.WriteLine("S2 — dynamic component rendering under static SSR");
Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  ·  .NET {Environment.Version}  ·  {address}");
Console.WriteLine("Razor components registered WITHOUT any interactive render mode.");

// ---------------------------------------------------------------------------------------------
Check.Section("1. The three-level composition renders");

var healthy = await GetAsync(1);

Check.That(healthy.Html.Contains("Ship faster", StringComparison.Ordinal),
    "template → zone → blocks → block component: the hero headline renders");
Check.That(healthy.Html.Contains("It cut our publish cycle in half.", StringComparison.Ordinal),
    "a second block type in the same zone renders");
Check.That(healthy.Html.Contains("/media/812/1280x720/cover/hero.webp", StringComparison.Ordinal),
    "a block hosting a field renderer renders one level deeper still (block → media renderer)");
Check.That(healthy.Html.Contains("Shared footer", StringComparison.Ordinal),
    "an asynchronous field renderer (reusable content) completes before the response is written");
Check.That(healthy.Html.Contains("Resolved after an await.", StringComparison.Ordinal),
    "a block that awaits inside OnParametersSetAsync renders its resolved content");
Check.That(!healthy.Html.Contains("data-zone=\"aside\"", StringComparison.Ordinal),
    "a zone declared by the template but absent from the payload renders nothing");

Check.Section("2. The output is genuinely static");

Check.That(!healthy.Html.Contains("<!--Blazor:", StringComparison.Ordinal),
    "no Blazor component markers in the output — nothing to hydrate");
Check.That(!healthy.Html.Contains("blazor.web.js", StringComparison.Ordinal),
    "no Blazor script reference");
Check.That(!healthy.Html.Contains("_framework", StringComparison.Ordinal),
    "no framework asset references");

// ---------------------------------------------------------------------------------------------
Check.Section("3. The spec §15.3 fallback matrix");

var unknownTemplate = await GetAsync(2);
Check.That(unknownTemplate.StatusCode == 200 && unknownTemplate.Html.Contains("data-cms-fallback=\"template\"", StringComparison.Ordinal),
    "unknown templateKey → minimal fallback layout, HTTP 200");
Check.That(unknownTemplate.Html.Contains("Ship faster", StringComparison.Ordinal),
    "the fallback still surfaces the page's text content");
Check.That(unknownTemplate.Diagnostics.Any(d => d.StartsWith("template.unknown", StringComparison.Ordinal)),
    "the unknown template is recorded for the cms-templates health check");

var unknownField = await GetAsync(3);
Check.That(unknownField.StatusCode == 200,
    "unknown field type key → the request still succeeds");
Check.That(unknownField.Html.Contains("The rest of the page still renders.", StringComparison.Ordinal),
    "unknown field type key → the other zones render");
Check.That(unknownField.Diagnostics.Any(d => d.Contains("fieldType.unknown key=sparkline", StringComparison.Ordinal)),
    "unknown field type key → logged as a warning, not thrown");

var unknownBlock = await GetAsync(8);
Check.That(unknownBlock.Html.Contains("Sibling before the failure.", StringComparison.Ordinal) &&
           unknownBlock.Html.Contains("Sibling after the failure.", StringComparison.Ordinal),
    "unknown block type → skipped, siblings render");
Check.That(unknownBlock.Diagnostics.Any(d => d.Contains("blockType.unknown key=carousel", StringComparison.Ordinal)),
    "unknown block type → logged");

var brokenReferences = await GetAsync(7);
Check.That(brokenReferences.Html.Contains("image unavailable", StringComparison.Ordinal) &&
           brokenReferences.Html.Contains("A diagram that went missing", StringComparison.Ordinal),
    "missing media → placeholder carrying the alt text");
Check.That(brokenReferences.Diagnostics.Any(d => d.Contains("media.missing id=404", StringComparison.Ordinal)),
    "missing media → logged");
Check.That(!brokenReferences.Html.Contains("<aside", StringComparison.Ordinal) &&
           brokenReferences.Diagnostics.Any(d => d.Contains("reusable.unpublished id=9", StringComparison.Ordinal)),
    "unpublished reusable content → renders nothing, logs a warning");
Check.That(brokenReferences.Html.Contains("Body survives both failures.", StringComparison.Ordinal),
    "the rest of the page is unaffected by both broken references");

// ---------------------------------------------------------------------------------------------
Check.Section("4. Error boundaries isolate a failing block");

foreach (var (pageId, label) in new[]
{
    (4L, "throws from OnParametersSet"),
    (5L, "throws from BuildRenderTree"),
    (6L, "throws from OnParametersSetAsync, post-await"),
})
{
    var response = await GetAsync(pageId);

    Check.That(response.StatusCode == 200, $"a block that {label} still returns HTTP 200");
    Check.That(
        response.Html.Contains("Sibling before the failure.", StringComparison.Ordinal) &&
        response.Html.Contains("Sibling after the failure.", StringComparison.Ordinal),
        $"a block that {label} does not take its siblings down");
    Check.That(response.Html.Contains("Body after a failing block.", StringComparison.Ordinal),
        $"a block that {label} does not take the other zones down");
    Check.That(response.Html.Contains("data-cms-block-failed", StringComparison.Ordinal),
        $"a block that {label} is replaced by its boundary's fallback content");

    var failure = response.Diagnostics.FirstOrDefault(d => d.StartsWith("render.failure", StringComparison.Ordinal));
    Check.That(
        failure is not null &&
        failure.Contains("zone=hero", StringComparison.Ordinal) &&
        failure.Contains($"page={pageId}", StringComparison.Ordinal) &&
        failure.Contains($"version={1000 + pageId}", StringComparison.Ordinal) &&
        failure.Contains("block=22222222-2222-4222-8222-222222222222", StringComparison.Ordinal),
        $"the failure is logged with page id, zone key, version id, and block id",
        failure ?? "(nothing logged)");
}

var failedBlockPage = await GetAsync(4);
Check.Note("Rendered hero zone for the page whose middle block throws:");

foreach (var line in HeroFragment(failedBlockPage.Html))
{
    Console.WriteLine($"        │ {line}");
}

var partialOutput = await GetAsync(5);
Check.That(
    !partialOutput.Html.Contains("data-cms-partial-block-output", StringComparison.Ordinal),
    "markup already emitted by a block that then throws mid-render does not leak into the response",
    "the boundary discards the failing subtree rather than flushing half of it");

// ---------------------------------------------------------------------------------------------
Check.Section("5. Cache tags accumulated during render");

Check.That(
    healthy.CacheTags.Contains("media:812") && healthy.CacheTags.Contains("ru:3"),
    "render-time tags reach the response when the render completes before the write",
    $"X-Cms-Cache-Tags: {string.Join(", ", healthy.CacheTags)}");

var viaComponentResult = await GetViaComponentResultAsync(1);

Check.That(
    viaComponentResult.Html.Contains("Ship faster", StringComparison.Ordinal),
    "RazorComponentResult renders the same composition");

Check.That(
    viaComponentResult.CacheTags.Contains("media:812") && viaComponentResult.CacheTags.Contains("ru:3"),
    "RazorComponentResult also carries render-time tags set from Response.OnStarting",
    $"X-Cms-Cache-Tags: {string.Join(", ", viaComponentResult.CacheTags)} — the response is buffered, " +
    "so OnStarting still runs after the render completes. This holds only while nothing streams.");

// ---------------------------------------------------------------------------------------------
Check.Section("6. Render cost");

Console.WriteLine("        page                        blocks    p50        p95");

foreach (var (pageId, label, blocks) in new[]
{
    (10L, "typical marketing page", 2),
    (9L, "large page", 50),
})
{
    var page = SamplePages.All[pageId];
    var samples = new double[200];

    for (var i = 0; i < 50; i++)
    {
        _ = await RenderToStringAsync(app.Services, page);
    }

    for (var i = 0; i < samples.Length; i++)
    {
        var start = Stopwatch.GetTimestamp();
        _ = await RenderToStringAsync(app.Services, page);
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    Array.Sort(samples);
    Console.WriteLine(
        $"        {label,-26}  {blocks,6}    {Ms(samples[100]),-10} {Ms(samples[190])}");
}

Check.Note("Server render only — no HTTP, no output cache, no database.");

await app.StopAsync();

return Check.Summarize();

// ---------------------------------------------------------------------------------------------

async Task<Response> GetAsync(long id)
{
    diagnostics.Clear();
    var response = await client.GetAsync(new Uri($"/b/{id}", UriKind.Relative));
    var html = await response.Content.ReadAsStringAsync();

    return new Response((int)response.StatusCode, html, ReadTags(response), diagnostics.Entries);
}

async Task<Response> GetViaComponentResultAsync(long id)
{
    diagnostics.Clear();
    var response = await client.GetAsync(new Uri($"/a/{id}", UriKind.Relative));
    var html = await response.Content.ReadAsStringAsync();

    return new Response((int)response.StatusCode, html, ReadTags(response), diagnostics.Entries);
}

static IEnumerable<string> HeroFragment(string html)
{
    var start = html.IndexOf("<div class=\"blocks\"", StringComparison.Ordinal);
    var end = html.IndexOf("</div>", start, StringComparison.Ordinal);

    if (start < 0 || end < 0)
    {
        return ["(no blocks zone in the output)"];
    }

    return html[start..(end + 6)]
        .Replace("><", ">\n<", StringComparison.Ordinal)
        .Split('\n')
        .Select(l => l.Trim())
        .Where(l => l.Length > 0);
}

static IReadOnlyList<string> ReadTags(HttpResponseMessage response) =>
    response.Headers.TryGetValues("X-Cms-Cache-Tags", out var values)
        ? values.SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList()
        : [];

static async Task<(string Html, ISet<string> Tags)> RenderToStringAsync(IServiceProvider services, SamplePage page)
{
    using var document = page.Parse();
    var context = CreateContext(page, document);

    await using var scope = services.CreateAsyncScope();
    var templateType = ResolveTemplate(scope.ServiceProvider, page);

    await using var renderer = new HtmlRenderer(
        scope.ServiceProvider,
        scope.ServiceProvider.GetRequiredService<ILoggerFactory>());

    var html = await renderer.Dispatcher.InvokeAsync(async () =>
    {
        var output = await renderer.RenderComponentAsync<DeliveryHost>(
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Context"] = context,
                ["TemplateType"] = templateType,
            }));

        return output.ToHtmlString();
    });

    return (html, context.CacheTags);
}

static CmsRenderContext CreateContext(SamplePage page, JsonDocument document) =>
    new(page.Id, page.VersionId, page.TemplateKey, document.RootElement.Clone(), CmsRenderMode.Live,
        new HashSet<string>(StringComparer.Ordinal));

static Type ResolveTemplate(IServiceProvider services, SamplePage page)
{
    var registry = services.GetRequiredService<TemplateRegistry>();

    if (registry.TryResolve(page.TemplateKey, out var component))
    {
        return component;
    }

    services.GetRequiredService<RenderDiagnostics>()
        .Record($"template.unknown key={page.TemplateKey} page={page.Id}");

    return typeof(FallbackTemplate);
}

static string Ms(double milliseconds) =>
    milliseconds.ToString("N2", CultureInfo.InvariantCulture) + " ms";

internal sealed record Response(int StatusCode, string Html, IReadOnlyList<string> CacheTags, IReadOnlyList<string> Diagnostics);
