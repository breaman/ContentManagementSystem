using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The field renderers, one field type at a time (task P3-09, spec section 7.1).
/// </summary>
/// <remarks>
/// Every one of these renders through <see cref="CmsZone"/> and the real renderer catalog rather
/// than instantiating a component directly, because the dispatch and the markup are the same fact
/// from an author's point of view: a value that reaches the wrong renderer and a renderer that draws
/// the wrong thing look identical on the page.
/// <para>
/// The recurring assertion across the whole file is that a malformed, absent, or stale value renders
/// nothing and does not throw. That is spec section 15.3 applied one field type at a time, and it is
/// the property that decides whether a bad payload costs a paragraph or a page.
/// </para>
/// </remarks>
public class FieldRendererTests : IDisposable
{
    private readonly FieldRendererHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PlainTextIsEncodedRatherThanStripped()
    {
        // The field type stores what the author typed, angle brackets included, which leaves the
        // encoding here as the only thing between a stored '<' and an injected element.
        var markup = _harness.Render("""{"type":"plainText","value":"a < b & c"}""");

        markup.Should().Contain("a &lt; b &amp; c").And.NotContain("<b");
    }

    [Fact]
    public void MultilineTextKeepsItsLineBreaksAsMarkup()
    {
        var markup = _harness.Render("""{"type":"multilineText","value":"First\r\nSecond\nThird"}""");

        markup.Should().Contain("First").And.Contain("Second").And.Contain("Third");
        System.Text.RegularExpressions.Regex.Matches(markup, "<br").Should().HaveCount(2,
            "a Windows line ending is one break, not two");
    }

    [Fact]
    public void MarkdownRichTextIsConvertedAndSanitizedOnTheWayOut()
    {
        // richText stores markdown exactly as authored and never sanitizes it on write, so this is
        // the only pass there is (task P1-19, ADR 0008).
        var markup = _harness.Render(
            """{"type":"richText","format":"markdown","value":"**Ship** <script>alert(1)</script>"}""");

        markup.Should().Contain("<strong>Ship</strong>").And.NotContain("<script");
    }

    [Fact]
    public void HtmlRichTextIsSanitizedAgainOnRender()
    {
        // A row that reached the database through an import or a restore never passed the write-time
        // check; only this pass covers it.
        var markup = _harness.Render(
            """{"type":"richText","format":"html","value":"<p onclick=\"steal()\">Hi</p>"}""");

        markup.Should().Contain("Hi").And.NotContain("onclick");
    }

    [Fact]
    public void RichTextWithNoReadableFormatRendersNothingAndLogs()
    {
        // Guessing is not available: markdown rendered as HTML shows its source, and HTML rendered
        // as markdown escapes its markup.
        var markup = _harness.Render("""{"type":"richText","value":"# Title"}""");

        markup.Should().BeEmpty();
        _harness.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void TheConfiguredProfileWidensWhatRichTextMayRender()
    {
        // Extended is Basic plus tables, images, and layout containers; a figure is on that list
        // and nowhere on Basic's.
        const string zone = """{"type":"richText","format":"html","value":"<figure>Diagram</figure>"}""";

        _harness.Render(zone, FieldRendererHarness.Schema(FieldTypeKeys.RichText))
            .Should().NotContain("<figure", "Basic carries no figure, so the tag is unwrapped to its text");

        _harness.Render(zone, FieldRendererHarness.Schema(FieldTypeKeys.RichText, """{"profile":"extended"}"""))
            .Should().Contain("<figure");
    }

    [Fact]
    public void AnUnrecognisedProfileFallsBackToTheMostRestrictiveOne()
    {
        // A mistyped setting may only ever strip more than intended, never less.
        var markup = _harness.Render(
            """{"type":"richText","format":"html","value":"<figure>Diagram</figure>"}""",
            FieldRendererHarness.Schema(FieldTypeKeys.RichText, """{"profile":"anything"}"""));

        markup.Should().NotContain("<figure").And.Contain("Diagram");
    }

    [Fact]
    public void RawHtmlIsSanitizedUnderTheDeveloperProfile()
    {
        // The role that lets someone author this widens the allowlist; it does not remove it.
        var markup = _harness.Render(
            """{"type":"html","value":"<div class=\"embed\">Widget</div><script>alert(1)</script>"}""");

        markup.Should().Contain("Widget").And.NotContain("<script");
    }

    [Fact]
    public void ANumberIsEmittedExactlyAsStored()
    {
        // Precision the author chose survives, and nothing here depends on the server's culture.
        _harness.Render("""{"type":"number","value":12.50}""").Should().Contain("12.50");
        _harness.Render("""{"type":"number","value":1234567}""").Should().Contain("1234567")
            .And.NotContain(",");
    }

    [Fact]
    public void FalseRendersAndRendersDifferentlyFromAbsent()
    {
        // The field type treats a deliberate "off" as a filled value; a renderer that emitted
        // nothing for it would lose the author's answer.
        _harness.Render("""{"type":"boolean","value":false}""").Should().Contain("data-value=\"false\"");
        _harness.Render("""{"type":"boolean","value":true}""").Should().Contain("data-value=\"true\"");
        _harness.Render("""{"type":"boolean"}""").Should().BeEmpty();
    }

    [Fact]
    public void ADateCarriesBothTheMachineValueAndTheReadableOne()
    {
        var markup = _harness.Render("""{"type":"date","value":"2026-08-12"}""");

        markup.Should().Contain("datetime=\"2026-08-12\"").And.Contain("August 12, 2026");
    }

    [Fact]
    public void ADateIsNeverShiftedByATimeZone()
    {
        // "The 12th" means the 12th wherever it is read. Converting it is how a "published on" date
        // ends up a day out.
        _harness.Render("""{"type":"date","value":"2026-01-01"}""").Should().Contain("January 1, 2026");
        _harness.Render("""{"type":"date","value":"2026-12-31"}""").Should().Contain("December 31, 2026");
    }

    [Fact]
    public void ADateThatIsNotStoredInTheOneAcceptedFormRendersNothingAndLogs()
    {
        var markup = _harness.Render("""{"type":"date","value":"12/08/2026"}""");

        markup.Should().BeEmpty();
        _harness.Logs.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void AnInstantIsShownInUtcAndSaysSo()
    {
        // A time shown without naming its zone is the one presentation that is actively wrong.
        var markup = _harness.Render("""{"type":"dateTime","value":"2026-08-12T09:30:00+02:00"}""");

        markup.Should().Contain("7:30 AM UTC")
            .And.Contain("datetime=\"2026-08-12T09:30:00+02:00\"",
                "the stored offset still reaches the browser");
    }

    [Fact]
    public void AChoiceRendersTheShapeThatWasStoredRatherThanTheOneConfigured()
    {
        // A property narrowed from multiple to single still has pages holding arrays.
        var single = _harness.Render("""{"type":"choice","value":"wide"}""");
        var multiple = _harness.Render("""{"type":"choice","value":["wide","boxed"]}""");

        single.Should().Contain("wide").And.NotContain("<ul");
        multiple.Should().Contain("<ul").And.Contain("wide").And.Contain("boxed");
    }

    [Fact]
    public void AColourIsCarriedAsDataRatherThanAsAnInlineStyle()
    {
        // Emitting style="" from stored content is the one place the CSP would have to be relaxed
        // for authored data.
        var markup = _harness.Render("""{"type":"color","value":"#1f6feb"}""");

        markup.Should().Contain("data-color=\"#1f6feb\"").And.NotContain("style=");
    }

    [Fact]
    public void JsonRendersNothingAndSaysNothingAboutIt()
    {
        // The empty render is the feature: json is data for a block's markup to read, and printing
        // it would put internal structure onto a public page. Deliberate, so not logged.
        var markup = _harness.Render("""{"type":"json","value":{"secret":"internal"}}""");

        markup.Should().BeEmpty();
        _harness.Logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public void TagsRenderAsAListInTheOrderTheyWereAuthored()
    {
        var markup = _harness.Render("""{"type":"tags","value":["release-notes","v2"]}""");

        markup.Should().Contain("release-notes").And.Contain("v2");
        markup.IndexOf("release-notes", StringComparison.Ordinal).Should()
            .BeLessThan(markup.IndexOf("v2", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryValueShapedFieldTypeSurvivesAValueOfTheWrongKind()
    {
        // One assertion said many ways, and the one that matters most: a payload that disagrees with
        // its field type costs a zone, never a page (spec section 15.3).
        string[] zones =
        [
            """{"type":"plainText","value":42}""",
            """{"type":"multilineText","value":{"nested":true}}""",
            """{"type":"richText","format":"markdown","value":["a"]}""",
            """{"type":"html","value":7}""",
            """{"type":"number","value":"12.5"}""",
            """{"type":"boolean","value":"true"}""",
            """{"type":"date","value":20260812}""",
            """{"type":"dateTime","value":false}""",
            """{"type":"choice","value":{"picked":"wide"}}""",
            """{"type":"color","value":9}""",
            """{"type":"tags","value":"release-notes"}""",
            """{"type":"media","mediaId":"812"}""",
            """{"type":"mediaList","items":{"first":1}}""",
            """{"type":"link","kind":42}""",
            """{"type":"pageReference","value":"44"}""",
            """{"type":"reusable","reusableContentId":null}""",
            """{"type":"blocks","items":"none"}""",
        ];

        foreach (var zone in zones)
        {
            var render = () => _harness.Render(zone);

            render.Should().NotThrow(zone);
            render().Should().BeEmpty(zone);
        }
    }
}
