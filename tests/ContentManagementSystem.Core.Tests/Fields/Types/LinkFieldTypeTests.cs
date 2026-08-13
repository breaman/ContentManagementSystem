using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>link</c> (task P1-11, spec section 7.1, decision D6).
/// </summary>
public class LinkFieldTypeTests
{
    private readonly LinkFieldType _fieldType = new();

    [Fact]
    public async Task AnInternalLinkIsAccepted()
    {
        var result = await _fieldType.ValidateAsync(
            """
            { "type": "link", "kind": "page", "pageId": 44, "text": "Get started",
              "target": "_self", "rel": null }
            """);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnInternalLinkWithoutAPageIsRejected()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "link", "kind": "page" }""");

        result.Codes().Should().Equal(FieldValidationCodes.ReferenceId);
        result.Paths().Should().Equal("pageId");
    }

    [Fact]
    public async Task ALinkWithNoKindIsUnfilledRatherThanMalformed()
    {
        var draft = await _fieldType.ValidateAsync("""{ "type": "link" }""", isRequired: true);
        var publish = await _fieldType.ValidateAsync(
            """{ "type": "link" }""",
            mode: ValidationMode.Publish,
            isRequired: true);

        draft.IsValid.Should().BeTrue();
        publish.Codes().Should().Equal(FieldValidationCodes.Required);
    }

    [Fact]
    public async Task AKindOutsideTheV1SetIsRejected()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "link", "kind": "telephone" }""");

        result.Codes().Should().Equal(FieldValidationCodes.LinkKind);
    }

    [Theory]
    [InlineData("https://example.com/pricing")]
    [InlineData("http://example.com")]
    public async Task AnExternalLinkOnAWebSchemeIsAccepted(string url)
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "link", "kind": "external", "url": "{{url}}" }""");

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("/pricing")]
    [InlineData("")]
    public async Task AnExternalLinkOnAnythingElseIsRejected(string url)
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "link", "kind": "external", "url": "{{url}}" }""");

        // The scheme allowlist is the security-relevant rule: without it the link picker is a route
        // to storing a script URL as a page's call to action.
        result.Codes().Should().Equal(FieldValidationCodes.LinkUrl);
    }

    [Fact]
    public async Task AMediaLinkIdentifiesTheFile()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "link", "kind": "media", "mediaId": 91, "text": "Download the report" }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task AnAnchorLinkNeedsAFragment()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "link", "kind": "anchor", "anchor": "" }""");

        result.Codes().Should().Equal(FieldValidationCodes.LinkAnchor);
    }

    [Theory]
    [InlineData("hello@example.com")]
    [InlineData("first.last+tag@sub.example.co.uk")]
    public async Task AnEmailLinkAcceptsAnAddressThatCouldBeDeliveredTo(string email)
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "link", "kind": "email", "email": "{{email}}" }""");

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("@example.com")]
    [InlineData("hello@")]
    [InlineData("two@at@example.com")]
    [InlineData("hello world@example.com")]
    public async Task AnEmailLinkRejectsWhatIsNotAnAddressAtAll(string email)
    {
        var result = await _fieldType.ValidateAsync(
            $$"""{ "type": "link", "kind": "email", "email": "{{email}}" }""");

        result.Codes().Should().Equal(FieldValidationCodes.LinkEmail);
    }

    [Fact]
    public async Task ATargetThatIsNotABrowsingContextIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "link", "kind": "page", "pageId": 44, "target": "_new" }""");

        result.Codes().Should().Equal(FieldValidationCodes.LinkTarget);
    }

    [Fact]
    public async Task AnAbsentTargetIsFine()
    {
        var result = await _fieldType.ValidateAsync("""{ "type": "link", "kind": "page", "pageId": 44 }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void AnInternalLinkIsReportedAsAPageReference()
    {
        var references = _fieldType.ExtractReferences("""{ "type": "link", "kind": "page", "pageId": 44 }""");

        // This is what makes moving a page safe: the URL is resolved from the page's current route
        // at render, and the reference is what tells the mover which pages to invalidate.
        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.Page, 44));
    }

    [Fact]
    public void AMediaLinkIsReportedAsAMediaReference()
    {
        var references = _fieldType.ExtractReferences("""{ "type": "link", "kind": "media", "mediaId": 91 }""");

        references.Should().ContainSingle()
            .Which.Should().Be(new ContentReference(ContentReferenceTargetType.Media, 91));
    }

    [Theory]
    [InlineData("""{ "type": "link", "kind": "external", "url": "https://example.com" }""")]
    [InlineData("""{ "type": "link", "kind": "anchor", "anchor": "pricing" }""")]
    [InlineData("""{ "type": "link", "kind": "email", "email": "hello@example.com" }""")]
    public void ALinkLeavingThisSiteReportsNothing(string property)
    {
        // Nothing here can move underneath the page, so there is nothing to invalidate.
        _fieldType.ExtractReferences(property).Should().BeEmpty();
    }

    [Fact]
    public void AnIdIsNotReportedUnderTheWrongKind()
    {
        var references = _fieldType.ExtractReferences(
            """{ "type": "link", "kind": "external", "url": "https://example.com", "pageId": 44 }""");

        // A leftover pageId from a picker the author changed their mind about. Reporting it would
        // put a dependency in ContentReference that the rendered page does not have.
        references.Should().BeEmpty();
    }

    [Fact]
    public void TheVisibleLabelIsIndexed()
    {
        var text = _fieldType.ExtractSearchText(FieldTypeTestHarness.Element(
            """{ "type": "link", "kind": "external", "url": "https://example.com", "text": "Read the report" }"""));

        // The label, not the destination: matching a page on a URL it links to returns the wrong
        // page for the query.
        text.Should().Be("Read the report");
    }
}
