using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Tests.Content;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The <c>blocks</c> renderer and the block-property dispatch under it (task P3-09,
/// spec section 8.2).
/// </summary>
/// <remarks>
/// This is where the content model's indirection finishes: a template names a zone, the zone
/// resolves to this renderer, and this resolves each item's stored block type key to a component
/// through the same scan the reconciler runs. Nothing above it learns what blocks a page contains.
/// </remarks>
public class BlocksRendererTests : IDisposable
{
    private const string Quote = """
        {"type":"blocks","items":[
            {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"quote","blockTypeRevision":2,
             "properties":{"quote":{"type":"plainText","value":"Ship faster"}}}
        ]}
        """;

    private readonly FieldRendererHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void EachItemIsRenderedByTheComponentDeclaringItsBlockTypeKey()
    {
        var markup = _harness.Render(Quote);

        markup.Should().Contain("<figure").And.Contain("Ship faster")
            .And.Contain("data-block=\"0f6c1f0a-0000-4000-8000-00000000000a\"")
            .And.Contain("data-revision=\"2\"");
    }

    [Test]
    public void ABlockPropertyGoesThroughItsOwnFieldTypesRenderer()
    {
        // The reason CmsBlockProperty exists: a block's structured properties are read by the field
        // renderer that wrote them, not picked apart by the block's markup.
        var markup = _harness.Render("""
            {"type":"blocks","items":[
                {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"quote",
                 "properties":{"quote":{"type":"plainText","value":"a < b"}}}
            ]}
            """);

        markup.Should().Contain("a &lt; b");
    }

    [Test]
    public void ABlockPropertySeesTheConfigurationItsRevisionCaptured()
    {
        // Resolved from the block type revision the instance named, not from whatever the block type
        // looks like today — the same rule a zone follows through its template revision.
        _harness.Schemas = new ContentSchemaCatalog(
            [],
            [
                ContentEngineHarness.BlockType(
                    "quote",
                    2,
                    ContentPropertySchema.Create("quote", "Quote", FieldTypeKeys.RichText,
                        """{"profile":"extended"}""")),
            ]);

        var markup = _harness.Render("""
            {"type":"blocks","items":[
                {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"quote","blockTypeRevision":2,
                 "properties":{"quote":{"type":"richText","format":"html","value":"<figure>Loud</figure>"}}}
            ]}
            """);

        markup.Should().Contain("<figure>Loud</figure>",
            "the captured profile is extended, which permits figure");
    }

    [Test]
    public void ABlockWhoseTypeThisDeploymentNoLongerCarriesIsSkippedAndTheRestRender()
    {
        // One retired block type must not blank a page (spec section 15.3).
        var markup = _harness.Render("""
            {"type":"blocks","items":[
                {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"retired-block",
                 "properties":{}},
                {"id":"0f6c1f0a-0000-4000-8000-00000000000b","blockTypeKey":"quote",
                 "properties":{"quote":{"type":"plainText","value":"Still here"}}}
            ]}
            """);

        markup.Should().Contain("Still here");
        _harness.Logs.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning && entry.Message.Contains("retired-block"));
    }

    [Test]
    public void ABlockNamingNoTypeAtAllIsSkippedAndLogged()
    {
        var markup = _harness.Render("""
            {"type":"blocks","items":[{"id":"0f6c1f0a-0000-4000-8000-00000000000a","properties":{}}]}
            """);

        markup.Should().BeEmpty();
        _harness.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    [Test]
    public void BlocksRenderInTheOrderTheyWereAuthored()
    {
        var markup = _harness.Render("""
            {"type":"blocks","items":[
                {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"quote",
                 "properties":{"quote":{"type":"plainText","value":"First"}}},
                {"id":"0f6c1f0a-0000-4000-8000-00000000000b","blockTypeKey":"quote",
                 "properties":{"quote":{"type":"plainText","value":"Second"}}}
            ]}
            """);

        markup.IndexOf("First", StringComparison.Ordinal).Should()
            .BeLessThan(markup.IndexOf("Second", StringComparison.Ordinal));
    }

    [Test]
    public void ABlockMissingAPropertyItsMarkupReadsRendersTheRestOfItself()
    {
        // A block authored against an older revision is simply missing what was added since.
        var markup = _harness.Render("""
            {"type":"blocks","items":[
                {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"quote","properties":{}}
            ]}
            """);

        markup.Should().Contain("<figure").And.NotContain("Ship faster");
        _harness.Logs.Entries.Should().BeEmpty("an unauthored property is ordinary, not a fault");
    }

    [Test]
    public void ANestedReferenceInsideABlockStillTagsThePageThatDependsOnIt()
    {
        // The failure a container renderer makes easy: rendering the nesting but losing the
        // dependency, so the page never invalidates when the media item changes.
        var context = RenderingHarness.Context(RenderingHarness.Payload(("hero", """
            {"type":"blocks","items":[
                {"id":"0f6c1f0a-0000-4000-8000-00000000000a","blockTypeKey":"quote",
                 "properties":{"quote":{"type":"media","mediaId":812}}}
            ]}
            """)));

        _harness.RenderIn(context, "hero");

        context.CacheTags.Contains(CacheTags.Media(812)).Should().BeTrue();
    }
}
