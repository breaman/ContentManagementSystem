using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// The authored-output accessibility rules of spec section 28 (task P9-10).
/// </summary>
/// <remarks>
/// Everything here is a warning, so the assertions are about codes rather than about a publish being
/// refused. The cases that matter most are the negative ones: a check that fires on well-formed
/// content is a check editors learn to ignore, and an ignored warning is worse than none.
/// </remarks>
public class AuthoredAccessibilityValidatorTests
{
    /// <summary>
    /// The two field types that carry the <c>Sanitizable</c> flag, plus one that does not.
    /// </summary>
    /// <remarks>
    /// The plain-text entry is what makes the negative case below mean anything: the validator asks
    /// the registry which values are markup rather than matching on a key, so a registry holding only
    /// the markup types could not tell the two apart.
    /// </remarks>
    private readonly AuthoredAccessibilityValidator _validator = new(
        new FieldTypeRegistry(
        [
            new RichTextFieldType(new SanitizationService()),
            new HtmlFieldType(new SanitizationService()),
            new PlainTextFieldType(),
            new BlocksFieldType(new Lazy<IFieldTypeRegistry>(() => null!)),
        ]));

    [Test]
    public void AnH2FollowedByAnH4IsReportedAsASkip()
    {
        var diagnostics = Validate("""<h2>Pricing</h2><p>Text.</p><h4>Enterprise</h4>""");

        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(AccessibilityCodes.HeadingSkipped);
    }

    [Test]
    public void ContentStartingAtH3IsASkipBecauseTheTemplateOwnsH1()
    {
        // The rich-text profile has no h1 at all, so authored content starts at h2 by construction.
        // Opening at h3 leaves a gap under the template's h1 that a reader navigating by level falls
        // straight through.
        Validate("""<h3>Enterprise</h3>""")
            .Should().ContainSingle().Which.Code.Should().Be(AccessibilityCodes.HeadingSkipped);
    }

    [Test]
    public void GoingBackUpALevelIsNotASkip()
    {
        // h2, h3, h3, h2 is an ordinary two-section document. Only descending by more than one is a
        // gap; coming back up closes a section.
        Validate("""<h2>A</h2><h3>A1</h3><h3>A2</h3><h2>B</h2><h3>B1</h3>""")
            .Should().BeEmpty();
    }

    [Test]
    public void TheHeadingSequenceRunsAcrossZonesRatherThanRestartingInEachOne()
    {
        // A reader moving by heading does not know where a zone ends, so an h2 in one followed by an
        // h4 in the next is the same gap it would be inside one.
        var diagnostics = Validate(
            ("intro", """<h2>Pricing</h2>"""),
            ("body", """<h4>Enterprise</h4>"""));

        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(AccessibilityCodes.HeadingSkipped);
    }

    [Test]
    [Arguments("click here")]
    [Arguments("Click here!")]
    [Arguments("read more")]
    [Arguments("Learn more →")]
    [Arguments("this link")]
    [Arguments("https://example.test/pricing")]
    [Arguments("www.example.test")]
    public void LinkTextThatSaysNothingIsReported(string text)
    {
        Validate($"""<p>See <a href="https://example.test">{text}</a>.</p>""")
            .Should().ContainSingle()
            .Which.Code.Should().Be(AccessibilityCodes.LinkTextUninformative);
    }

    [Test]
    [Arguments("our pricing page")]
    [Arguments("the enterprise plan")]
    [Arguments("Download the 2026 handbook")]
    public void LinkTextThatDescribesItsDestinationIsNotReported(string text)
    {
        Validate($"""<p>See <a href="https://example.test">{text}</a>.</p>""").Should().BeEmpty();
    }

    [Test]
    public void ATableWithNoHeaderCellsIsReportedOnce()
    {
        var diagnostics = Validate("""
            <table><tbody><tr><td>Plan</td><td>Price</td></tr><tr><td>Team</td><td>£20</td></tr></tbody></table>
            """);

        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(AccessibilityCodes.TableWithoutHeaders);
    }

    [Test]
    public void AHeaderCellWithNoScopeIsReported()
    {
        var diagnostics = Validate("""
            <table><thead><tr><th>Plan</th><th scope="col">Price</th></tr></thead>
            <tbody><tr><td>Team</td><td>£20</td></tr></tbody></table>
            """);

        // One of the two, not both: the second says what it heads.
        diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(AccessibilityCodes.TableHeaderWithoutScope);
    }

    [Test]
    public void AWellFormedTableIsNotReported()
    {
        Validate("""
            <table><caption>Plans</caption>
            <thead><tr><th scope="col">Plan</th><th scope="col">Price</th></tr></thead>
            <tbody><tr><th scope="row">Team</th><td>£20</td></tr></tbody></table>
            """).Should().BeEmpty();
    }

    [Test]
    public void MarkupNestedInsideABlockIsCheckedToo()
    {
        // A rich-text property inside a block is exactly as visible on the page as one in a zone, and
        // a check that only reached the top level would report a clean bill for a page built out of
        // blocks.
        var payload = ContentPayload.Parse($$"""
            {
              "schemaVersion": 1, "templateKey": "article", "templateRevision": 1,
              "zones": {
                "sections": {
                  "type": "blocks",
                  "value": [
                    { "blockType": "rich-text", "properties": {
                        "body": { "type": "richText", "format": "html", "value": "<h4>Buried</h4>" } } }
                  ]
                }
              }
            }
            """);

        _validator.Validate(payload).Should().ContainSingle()
            .Which.Code.Should().Be(AccessibilityCodes.HeadingSkipped);
    }

    [Test]
    public void EveryDiagnosticIsAWarning()
    {
        // The one accessibility rule that blocks a publish is alt text, and it lives elsewhere. A
        // publish an editor cannot complete because a link says "read more" is a publish that happens
        // through whatever route skips the check.
        var diagnostics = Validate("""
            <h4>Skipped</h4><p><a href="https://example.test">click here</a></p>
            <table><tr><td>x</td></tr></table>
            """);

        diagnostics.Should().HaveCountGreaterThan(2)
            .And.OnlyContain(diagnostic => diagnostic.Severity == ValidationSeverity.Warning);
    }

    [Test]
    public void ContentWithNoMarkupZonesIsNotWalkedAtAll()
    {
        var payload = ContentPayload.Parse("""
            { "schemaVersion": 1, "templateKey": "article", "templateRevision": 1,
              "zones": { "kicker": { "type": "plainText", "value": "<h4>not markup</h4>" } } }
            """);

        // A plain-text value is not Sanitizable, so its angle brackets are text an author typed and
        // are escaped on the way out. Parsing it as markup would report a heading nobody wrote.
        _validator.Validate(payload).Should().BeEmpty();
    }

    private IReadOnlyList<ValidationDiagnostic> Validate(string markup) =>
        Validate(("body", markup));

    private IReadOnlyList<ValidationDiagnostic> Validate(params (string Zone, string Markup)[] zones)
    {
        var body = string.Join(",\n", zones.Select(zone =>
            $$"""
              "{{zone.Zone}}": { "type": "richText", "format": "html", "value": {{System.Text.Json.JsonSerializer.Serialize(zone.Markup)}} }
              """));

        return _validator.Validate(ContentPayload.Parse($$"""
            { "schemaVersion": 1, "templateKey": "article", "templateRevision": 1,
              "zones": { {{body}} } }
            """));
    }
}
