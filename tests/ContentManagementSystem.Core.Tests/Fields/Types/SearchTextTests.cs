using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// The markup-to-index-text reduction used by <c>richText</c> and <c>html</c> (task P1-10).
/// </summary>
public class SearchTextTests
{
    private readonly HtmlFieldType _fieldType = new(new RecordingSanitizer());

    [Fact]
    public void TagsBecomeWordBoundaries()
    {
        // Stripping tags without leaving a separator indexes "onetwo", which matches neither word.
        Extract("<p>one</p><p>two</p>").Should().Be("one two");
    }

    [Fact]
    public void EntitiesAreDecoded()
    {
        Extract("<p>Ship &amp; save &lt;fast&gt;</p>").Should().Be("Ship & save <fast>");
    }

    [Fact]
    public void ScriptBodiesAreDroppedRatherThanIndexed()
    {
        Extract("<p>Ship</p><script>var track = 1;</script>").Should().Be("Ship");
    }

    [Fact]
    public void StyleBodiesAreDroppedRatherThanIndexed()
    {
        Extract("<style>.hero { color: red }</style><p>Ship</p>").Should().Be("Ship");
    }

    [Fact]
    public void AnUnclosedScriptDoesNotSwallowTheRestOfTheDocument()
    {
        // Truncated markup is exactly what an import or a hand-written value produces, and the
        // indexer must not lose a page's text to it.
        Extract("<p>Ship</p><script>var track = 1;").Should().Be("Ship");
    }

    [Fact]
    public void ProseContainingALessThanSignSurvives()
    {
        Extract("a < b and c > d").Should().Be("a < b and c > d");
    }

    [Fact]
    public void WhitespaceIsCollapsed()
    {
        Extract("<p>  Ship\n\n   faster  </p>").Should().Be("Ship faster");
    }

    [Fact]
    public void MarkupWithNoTextIndexesNothing()
    {
        Extract("<hr /><br />").Should().BeEmpty();
    }

    private string Extract(string html)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(html);

        return _fieldType.ExtractSearchText(
            FieldTypeTestHarness.Element($$"""{ "type": "html", "value": {{escaped}} }"""));
    }
}
