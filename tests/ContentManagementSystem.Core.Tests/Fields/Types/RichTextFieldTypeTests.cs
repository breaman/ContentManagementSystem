using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>richText</c> (task P1-10, spec sections 7.1 and 20.2).
/// </summary>
public class RichTextFieldTypeTests
{
    private readonly RecordingSanitizer _sanitizer = new();
    private readonly RichTextFieldType _fieldType;

    public RichTextFieldTypeTests() => _fieldType = new RichTextFieldType(_sanitizer);

    [Test]
    public async Task MarkdownAndHtmlAreBothAcceptedFormats()
    {
        var markdown = await _fieldType.ValidateAsync(
            """{ "type": "richText", "format": "markdown", "value": "We **help** teams" }""");
        var html = await _fieldType.ValidateAsync(
            """{ "type": "richText", "format": "html", "value": "<p>We help teams</p>" }""");

        markdown.IsValid.Should().BeTrue();
        html.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AValueWithNoFormatIsRejected()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "richText", "value": "**bold**" }""");

        // Neither reading degrades gracefully: rendered as HTML the markdown source shows through,
        // rendered as markdown the markup is escaped onto the page.
        result.Codes().Should().Equal(FieldValidationCodes.RichTextFormat);
    }

    [Test]
    public async Task AnUnrecognisedFormatIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "richText", "format": "textile", "value": "h1. Hello" }""");

        result.Codes().Should().Equal(FieldValidationCodes.RichTextFormat);
    }

    [Test]
    public async Task AnEmptyValueIsNotRejectedForItsFormat()
    {
        // Nothing has been authored yet, so complaining about the format of nothing is noise.
        var result = await _fieldType.ValidateAsync("""{ "type": "richText", "value": "" }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task LongerThanMaxLengthIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "richText", "format": "html", "value": "<p>far too long</p>" }""",
            """{ "maxLength": 10 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxLength);
    }

    [Test]
    public async Task AnUnknownProfileWarnsWithoutBlockingTheSave()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "richText", "format": "html", "value": "<p>hello</p>" }""",
            """{ "profile": "developer" }""");

        // Developer is unreachable from rich text by design: it permits iframes and data
        // attributes, and the role gate that justifies those lives on the html field type.
        result.Codes().Should().Equal(FieldValidationCodes.RichTextProfile);
        result.HasErrors.Should().BeFalse();
    }

    [Test]
    public async Task HtmlIsSanitizedOnTheWayIn()
    {
        _sanitizer.Transform = _ => "<p>clean</p>";

        var sanitized = await _fieldType.SanitizeAsync(
            """{ "type": "richText", "format": "html", "value": "<p>dirty<script>x()</script></p>" }""");

        sanitized.GetProperty("value").GetString().Should().Be("<p>clean</p>");
        _sanitizer.Calls.Should().ContainSingle()
            .Which.Profile.Should().Be(SanitizationProfile.Basic);
    }

    [Test]
    public async Task TheConfiguredProfileSelectsTheAllowlist()
    {
        await _fieldType.SanitizeAsync(
            """{ "type": "richText", "format": "html", "value": "<table><tr><td>x</td></tr></table>" }""",
            """{ "profile": "extended" }""");

        _sanitizer.Calls.Should().ContainSingle()
            .Which.Profile.Should().Be(SanitizationProfile.Extended);
    }

    [Test]
    public async Task AnUnknownProfileFallsBackToTheStrictestAllowlist()
    {
        await _fieldType.SanitizeAsync(
            """{ "type": "richText", "format": "html", "value": "<iframe src='x'></iframe>" }""",
            """{ "profile": "developer" }""");

        // A mistyped profile can only ever be more restrictive than intended, never less.
        _sanitizer.Calls.Should().ContainSingle()
            .Which.Profile.Should().Be(SanitizationProfile.Basic);
    }

    [Test]
    public async Task SanitizingLeavesTheOtherMembersOfTheValueAlone()
    {
        _sanitizer.Transform = _ => "<p>clean</p>";

        var sanitized = await _fieldType.SanitizeAsync(
            """{ "type": "richText", "format": "html", "value": "<p>dirty</p>", "authoredWith": "quill" }""");

        // A stored property can carry members written by a newer deployment. Dropping them on a
        // save would be silent data loss.
        sanitized.GetProperty("type").GetString().Should().Be("richText");
        sanitized.GetProperty("format").GetString().Should().Be("html");
        sanitized.GetProperty("authoredWith").GetString().Should().Be("quill");
    }

    [Test]
    public async Task MarkdownIsStoredExactlyAsAuthored()
    {
        _sanitizer.Transform = _ => "mangled";

        var sanitized = await _fieldType.SanitizeAsync(
            """{ "type": "richText", "format": "markdown", "value": "We **help** teams" }""");

        // Markdown is sanitized after conversion instead (P1-19). Rewriting the source to whatever
        // a round trip produces would lose the author's formatting on every save.
        sanitized.GetProperty("value").GetString().Should().Be("We **help** teams");
        _sanitizer.Calls.Should().BeEmpty();
    }

    [Test]
    public async Task AnUnchangedValueIsNotRewritten()
    {
        var property = FieldTypeTestHarness.Element(
            """{ "type": "richText", "format": "html", "value": "<p>already clean</p>" }""");

        var sanitized = await _fieldType.SanitizeAsync(
            property,
            FieldConfiguration.Empty,
            TestContext.Current!.Execution.CancellationToken);

        sanitized.GetRawText().Should().Be(property.GetRawText());
    }

    [Test]
    public void SearchTextDropsTheMarkup()
    {
        var property = FieldTypeTestHarness.Element(
            """{ "type": "richText", "format": "html", "value": "<h2>Ship</h2><p>faster &amp; safer</p>" }""");

        _fieldType.ExtractSearchText(property).Should().Be("Ship faster & safer");
    }

    [Test]
    public void RichTextIsSearchableAndSanitizable()
    {
        _fieldType.Capabilities.Should().Be(
            FieldTypeCapabilities.Searchable | FieldTypeCapabilities.Sanitizable);
    }
}
