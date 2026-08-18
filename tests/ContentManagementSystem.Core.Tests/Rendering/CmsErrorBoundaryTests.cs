using Bunit;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Rendering.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// One failing renderer does not take the page with it (task P3-11, acceptance criterion P3 #8).
/// </summary>
/// <remarks>
/// The S2 spike proved boundaries isolate a failure under static SSR in all three shapes a renderer
/// can fail in. What these hold in place is that the boundaries are actually <em>wired in</em>, at
/// both levels, and that the log line carries what an operator needs to find the content that caused
/// it — page id, zone key, version id, and block id. A boundary that catches silently turns a broken
/// page into a mystery.
/// </remarks>
public class CmsErrorBoundaryTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly RecordingLoggerProvider _logs = new();

    public CmsErrorBoundaryTests()
    {
        _bunit.Services.AddLogging(logging => logging.AddProvider(_logs));
        _bunit.Services.AddSingleton<IContentSchemaCatalog>(ContentSchemaCatalog.Empty);
        _bunit.Services.AddSingleton<IFieldRendererCatalog>(new TestFieldRendererCatalog(
            ("plainText", typeof(RecordingFieldRenderer)),
            ("throwsOnParameters", typeof(ThrowsOnParametersRenderer)),
            ("throwsWhileBuilding", typeof(ThrowsWhileBuildingRenderer)),
            ("throwsAfterAwait", typeof(ThrowsAfterAwaitRenderer)),
            ("blocks", typeof(BlocksRenderer))));
        _bunit.Services.AddSingleton<ICmsComponentCatalog>(new CmsComponentCatalog(
            CmsComponentScanner.ScanTypes(
                [typeof(BoundaryTemplate), typeof(TestBlock), typeof(ThrowingBlock)])));
    }

    public void Dispose()
    {
        _bunit.Dispose();
        _logs.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    [Arguments("throwsOnParameters")]
    [Arguments("throwsWhileBuilding")]
    [Arguments("throwsAfterAwait")]
    public void AZoneWhoseRendererThrowsLosesOnlyThatZone(string failingFieldTypeKey)
    {
        var page = RenderPage(
            ("first", $$"""{"type":"{{failingFieldTypeKey}}","value":"boom"}"""),
            ("second", """{"type":"plainText","value":"Still here"}"""));

        // Three different shapes of failure, one behaviour. The post-await case is the one a plain
        // try/catch around the render call misses, because the exception surfaces on a continuation
        // rather than on the calling stack — and it is also why this waits rather than asserting
        // outright: the boundary's own re-render is queued behind that continuation. Delivery does
        // not have to care, because HtmlRenderer.RenderComponentAsync waits for quiescence before
        // the markup is read at all.
        page.WaitForAssertion(() => page.Markup.Should()
            .Contain("data-cms-render-failed=\"zone\"")
            .And.Contain("data-cms-zone=\"first\""));

        page.Markup.Should().Contain("Still here");
    }

    [Test]
    public void AHalfWrittenSubtreeDoesNotReachThePage()
    {
        var markup = Render(("first", """{"type":"throwsWhileBuilding","value":"boom"}"""));

        // The renderer opens an element and then throws. Blazor discards the failing subtree rather
        // than flushing what it had, so nothing it started appears — a renderer that fails part-way
        // cannot corrupt the document around it.
        markup.Should().NotContain("half-written");
    }

    [Test]
    public void TheFailureIsLoggedWithThePageZoneAndVersion()
    {
        Render(("first", """{"type":"throwsOnParameters","value":"boom"}"""));

        // Acceptance criterion P3 #8, literally. A stack trace names a component; it does not name
        // which of four hundred pages built on that component was being rendered, nor which zone of
        // it, nor which version — and without those there is nothing to reproduce or to tell an
        // editor about.
        _logs.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("Zone") &&
            entry.Message.Contains("'first'") &&
            entry.Message.Contains("44") &&
            entry.Message.Contains("1004"));
    }

    [Test]
    public void ABlockThatThrowsLosesOnlyThatBlockAndItsSiblingsStillRender()
    {
        var markup = Render(("first", $$"""
            {
              "type": "blocks",
              "items": [
                { "id": "11111111-1111-4111-8111-111111111111", "blockTypeKey": "test-block",
                  "blockTypeRevision": 1, "properties": { "headline": { "type": "plainText", "value": "Before" } } },
                { "id": "22222222-2222-4222-8222-222222222222", "blockTypeKey": "throwing-block",
                  "blockTypeRevision": 1, "properties": {} },
                { "id": "33333333-3333-4333-8333-333333333333", "blockTypeKey": "test-block",
                  "blockTypeRevision": 1, "properties": { "headline": { "type": "plainText", "value": "After" } } }
              ]
            }
            """));

        // The reason there is a boundary per block and not only per zone. Caught one level up, this
        // failure would have blanked the whole list — for a body zone, most of the page.
        markup.Should().Contain("Before").And.Contain("After");
        markup.Should().Contain("data-cms-render-failed=\"block\"");
        markup.Should().Contain("data-cms-block=\"22222222-2222-4222-8222-222222222222\"");
    }

    [Test]
    public void ABlockFailureNamesTheBlockAndItsTypeInTheLog()
    {
        Render(("first", """
            {
              "type": "blocks",
              "items": [
                { "id": "22222222-2222-4222-8222-222222222222", "blockTypeKey": "throwing-block",
                  "blockTypeRevision": 1, "properties": {} }
              ]
            }
            """));

        _logs.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error &&
            entry.Message.Contains("Block") &&
            entry.Message.Contains("22222222-2222-4222-8222-222222222222") &&
            entry.Message.Contains("throwing-block"));
    }

    [Test]
    public async Task TheDeliveryRenderPathSeesTheBoundaryMarkerToo()
    {
        // The delivery path is HtmlRenderer-to-string, not bUnit, and the difference matters for
        // exactly the case above: RenderComponentAsync waits for quiescence, so the boundary's own
        // re-render is already in the tree by the time the markup is read. This asserts that rather
        // than assuming it, using the async failure — the one shape where the ordering is in doubt.
        await using var services = BuildDeliveryServices();

        var context = new RenderContext(
            RenderingHarness.Page(BoundaryTemplate.Key),
            RenderingHarness.PayloadFor(
                BoundaryTemplate.Key,
                ("first", """{"type":"throwsAfterAwait","value":"boom"}"""),
                ("second", """{"type":"plainText","value":"Still here"}""")));

        await using var renderer = new HtmlRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>());

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<CmsPageHost>(
                ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    [nameof(CmsPageHost.Context)] = context,
                }));

            return output.ToHtmlString();
        });

        html.Should().Contain("Still here").And.Contain("data-cms-render-failed=\"zone\"");
    }

    /// <summary>The same registrations as the bUnit context, in a container of their own.</summary>
    private ServiceProvider BuildDeliveryServices() =>
        new ServiceCollection()
            .AddLogging(logging => logging.AddProvider(_logs))
            .AddSingleton<IContentSchemaCatalog>(ContentSchemaCatalog.Empty)
            .AddSingleton<IFieldRendererCatalog>(new TestFieldRendererCatalog(
                ("plainText", typeof(RecordingFieldRenderer)),
                ("throwsAfterAwait", typeof(ThrowsAfterAwaitRenderer))))
            .AddSingleton<ICmsComponentCatalog>(new CmsComponentCatalog(
                CmsComponentScanner.ScanTypes([typeof(BoundaryTemplate)])))
            .BuildServiceProvider();

    private string Render(params (string Key, string Json)[] zones) => RenderPage(zones).Markup;

    private IRenderedComponent<CmsPageHost> RenderPage(params (string Key, string Json)[] zones)
    {
        var context = new RenderContext(
            RenderingHarness.Page(BoundaryTemplate.Key),
            RenderingHarness.PayloadFor(BoundaryTemplate.Key, zones));

        return _bunit.Render<CmsPageHost>(parameters => parameters
            .Add(host => host.Context, context));
    }
}

/// <summary>A template with two zones, so zone-level isolation has something to be isolated from.</summary>
[CmsTemplate(Key, "Boundary Template")]
internal sealed class BoundaryTemplate : CmsTemplateBase
{
    public const string Key = "boundary-template";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "article");
        builder.OpenComponent<CmsZone>(1);
        builder.AddComponentParameter(2, nameof(CmsZone.Name), "first");
        builder.CloseComponent();
        builder.OpenComponent<CmsZone>(3);
        builder.AddComponentParameter(4, nameof(CmsZone.Name), "second");
        builder.CloseComponent();
        builder.CloseElement();
    }
}

/// <summary>Fails in a lifecycle method, before any markup is built.</summary>
internal sealed class ThrowsOnParametersRenderer : CmsFieldRendererBase
{
    protected override void OnParametersSet() =>
        throw new InvalidOperationException("Renderer failed while reading its parameters.");
}

/// <summary>
/// Fails part-way through building its markup, having already emitted an element.
/// </summary>
/// <remarks>
/// The shape worth testing separately: it is the one where a naive implementation could flush what
/// it had already written and leave an unclosed element in the response.
/// </remarks>
internal sealed class ThrowsWhileBuildingRenderer : CmsFieldRendererBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "half-written");
        builder.AddContent(2, "half-written");

        throw new InvalidOperationException("Renderer failed mid-build.");
    }
}

/// <summary>
/// Fails after an await, so the exception surfaces on a continuation rather than on the caller.
/// </summary>
internal sealed class ThrowsAfterAwaitRenderer : CmsFieldRendererBase
{
    protected override async Task OnParametersSetAsync()
    {
        await Task.Yield();

        throw new InvalidOperationException("Renderer failed after awaiting.");
    }
}

/// <summary>A block whose markup throws, for the per-block boundary.</summary>
[CmsBlockType("throwing-block", "Throwing Block")]
internal sealed class ThrowingBlock : CmsBlockBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        throw new InvalidOperationException("Block failed while rendering.");
}
