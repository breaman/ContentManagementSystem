using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>html</c> (task P1-10, spec sections 7.1 and 20.2).
/// </summary>
public class HtmlFieldTypeTests
{
    private readonly RecordingSanitizer _sanitizer = new();
    private readonly HtmlFieldType _fieldType;

    public HtmlFieldTypeTests() => _fieldType = new HtmlFieldType(_sanitizer);

    [Test]
    public async Task MarkupIsAccepted()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "html", "value": "<iframe src=\"https://example.test/embed\"></iframe>" }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task LongerThanMaxLengthIsRejected()
    {
        var result = await _fieldType.ValidateAsync(
            """{ "type": "html", "value": "<p>far too long</p>" }""",
            """{ "maxLength": 8 }""");

        result.Codes().Should().Equal(FieldValidationCodes.MaxLength);
    }

    [Test]
    public async Task TheDeveloperAllowlistIsApplied()
    {
        _sanitizer.Transform = _ => "<iframe src=\"https://example.test/embed\"></iframe>";

        await _fieldType.SanitizeAsync(
            """{ "type": "html", "value": "<iframe src=\"https://example.test/embed\"></iframe><script>x()</script>" }""");

        _sanitizer.Calls.Should().ContainSingle()
            .Which.Profile.Should().Be(SanitizationProfile.Developer);
    }

    [Test]
    public async Task MarkupIsStillSanitizedDespiteTheRoleRestriction()
    {
        _sanitizer.Transform = _ => "<p>clean</p>";

        var sanitized = await _fieldType.SanitizeAsync(
            """{ "type": "html", "value": "<p>clean</p><script>x()</script>" }""");

        // A role is an authorization decision. Treating it as licence to store markup unchecked
        // makes every later privilege mistake a stored-XSS incident.
        sanitized.GetProperty("value").GetString().Should().Be("<p>clean</p>");
    }

    [Test]
    public async Task AnEmptyValueIsNotHandedToTheSanitizer()
    {
        await _fieldType.SanitizeAsync("""{ "type": "html", "value": "" }""");

        _sanitizer.Calls.Should().BeEmpty();
    }

    [Test]
    public void SearchTextDropsScriptAndStyleBodiesRatherThanIndexingThem()
    {
        var property = FieldTypeTestHarness.Element(
            """{ "type": "html", "value": "<style>.a{color:red}</style><p>Ship</p><script>track()</script>" }""");

        _fieldType.ExtractSearchText(property).Should().Be("Ship");
    }

    [Test]
    public void HtmlIsRestrictedToDevelopers()
    {
        _fieldType.Capabilities.Should().HaveFlag(FieldTypeCapabilities.DeveloperOnly);
        _fieldType.Capabilities.Should().HaveFlag(FieldTypeCapabilities.Sanitizable);
    }
}
