using Bunit;

using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Rendering;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// Composition from the root down: host → template → zone → field renderer (task P3-08).
/// </summary>
/// <remarks>
/// The S2 spike proved this composes under static SSR with nothing switched on statically. What
/// these tests hold in place is that it stays that way — a template names a zone key and nothing
/// else, and every hop is a lookup.
/// </remarks>
public class CmsPageHostTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    private readonly RecordingLoggerProvider _logs = new();

    public CmsPageHostTests()
    {
        _bunit.Services.AddLogging(logging => logging.AddProvider(_logs));
        _bunit.Services.AddSingleton<IFieldRendererCatalog>(new TestFieldRendererCatalog(
            ("plainText", typeof(RecordingFieldRenderer))));
        _bunit.Services.AddSingleton<ICmsComponentCatalog>(new CmsComponentCatalog(
            CmsComponentScanner.ScanTypes([typeof(TestTemplate), typeof(TestBlock)])));

        // The real registry, which is what CmsFallbackTemplate reduces a payload to text through.
        // A hand-picked one would answer "no field type" for the values these tests are about and
        // the fallback would render an empty article that still passed a weaker assertion.
        _bunit.Services.AddSingleton(FieldRendererHarness.Registry);
    }

    public void Dispose()
    {
        _bunit.Dispose();
        _logs.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ATemplateResolvedFromTheStoredKeyRendersItsZonesThroughTheCascadedContext()
    {
        var context = RenderingHarness.Context(RenderingHarness.Payload(
            ("hero", """{"type":"plainText","value":"Hello"}""")));

        var markup = _bunit.Render<CmsPageHost>(parameters => parameters
            .Add(host => host.Context, context)).Markup;

        // Four levels in one assertion: the host resolved the template by key, the template read the
        // page off the cascaded context, the zone resolved a renderer, and the renderer read the
        // stored value.
        markup.Should().Contain("<article")
            .And.Contain("data-title=\"About us\"")
            .And.Contain("data-renderer=\"recording\"")
            .And.Contain("Hello");
    }

    [Fact]
    public void AnUnknownTemplateKeyRendersTheFallbackLayoutAndLogsAnErrorRatherThanThrowing()
    {
        var context = RenderingHarness.Context(
            RenderingHarness.PayloadFor(
                "template-this-deployment-lost",
                ("hero", """{"type":"plainText","value":"The words on the page"}""")),
            templateKey: "template-this-deployment-lost");

        var markup = _bunit.Render<CmsPageHost>(parameters => parameters
            .Add(host => host.Context, context)).Markup;

        // Spec section 15.3's first row, completed in task P3-11: a minimal layout carrying the
        // page's text content. A blank response and a stack trace are both worse than plain words,
        // and a template component can go missing for entirely ordinary reasons.
        markup.Should().Contain("data-cms-fallback=\"template\"")
            .And.Contain("data-template-missing=\"template-this-deployment-lost\"")
            .And.Contain("About us")
            .And.Contain("The words on the page");

        _logs.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Error && entry.Message.Contains("template-this-deployment-lost"));
    }

    [Fact]
    public void TheFallbackAsksTheFieldTypesForTheTextRatherThanReadingTheJson()
    {
        var context = RenderingHarness.Context(
            RenderingHarness.PayloadFor(
                "template-this-deployment-lost",
                ("body", """
                    {
                      "type": "blocks",
                      "items": [
                        { "id": "11111111-1111-4111-8111-111111111111", "blockTypeKey": "quote",
                          "blockTypeRevision": 1,
                          "properties": { "quote": { "type": "plainText", "value": "Buried two levels down" } } }
                      ]
                    }
                    """),
                ("poster", """{"type":"media","mediaId":812}""")),
            templateKey: "template-this-deployment-lost");

        var markup = _bunit.Render<CmsPageHost>(parameters => parameters
            .Add(host => host.Context, context)).Markup;

        // Dispatched through ExtractSearchText on the field type each value names, which is the only
        // way to get readable text out of a payload whose shapes are runtime data. A walk written by
        // hand would have to know that a block list nests its words two levels down — and would be
        // wrong about the next field type somebody adds.
        markup.Should().Contain("Buried two levels down");

        // And a media reference contributes nothing, because it has no words. Nothing here prints
        // raw JSON as a consolation.
        markup.Should().NotContain("812");
    }
}
