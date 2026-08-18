using Bunit;

using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Canvas;

/// <summary>
/// The card body drawn for a field type with no editor of its own (task P6-05).
/// </summary>
/// <remarks>
/// It stopped being what every zone gets when P6-06 to P6-15 shipped their editors, and became two
/// narrower things: the catalog's fallback for a field type this build has never heard of, and R13's
/// plain UI if Phase 6 is cut back to its acceptance criteria. Both only work if it still
/// round-trips a value correctly, which is what these assert.
/// </remarks>
public class PlainZoneEditorTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void ATextZoneStoresWhatIsTypedIntoItInsideTheEnvelope()
    {
        var raised = string.Empty;

        var editor = Render(
            FieldTypeKeys.PlainText,
            """{"type":"plainText","value":"before"}""",
            value => raised = value);

        editor.Find("textarea").Change("after");

        raised.Should().Be("""{"type":"plainText","value":"after"}""");
    }

    [Test]
    public void RichTextIsNotEditableHereBecauseItsFormatWouldBeLost()
    {
        var editor = Render(
            FieldTypeKeys.RichText,
            """{"type":"richText","format":"markdown","value":"Hi"}""",
            _ => { });

        // Its stored value carries a format beside its text, and a textarea writing the text back
        // without the format would leave the value uninterpretable — the field type treats an absent
        // format as an error rather than as a default.
        editor.Find("textarea").HasAttribute("readonly").Should().BeTrue();
    }

    [Test]
    public void AFieldTypeWithNoEditorIsShownAsStoredAndNotEditable()
    {
        var editor = Render(FieldTypeKeys.Blocks, """{ "type": "blocks", "items": [] }""", _ => { });

        var textarea = editor.Find("textarea");

        // Read-only rather than absent: an empty card is indistinguishable from an empty value, and
        // inventing a control would mean inventing a shape for the value.
        textarea.HasAttribute("readonly").Should().BeTrue();
        textarea.GetAttribute("value").Should().Contain("items");
        editor.Markup.Should().Contain("No editor is registered");
    }

    [Test]
    public void ADisabledZoneCannotBeTypedInto()
    {
        var editor = Render(
            FieldTypeKeys.PlainText,
            """{"type":"plainText","value":"v"}""",
            _ => { },
            disabled: true);

        editor.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Test]
    public void TheControlIsNamedAndDescribedByTheCardAroundIt()
    {
        var editor = Render(FieldTypeKeys.PlainText, """{"type":"plainText","value":"v"}""", _ => { });

        var textarea = editor.Find("textarea");

        textarea.Id.Should().Be("zone-hero-control");
        textarea.GetAttribute("aria-labelledby").Should().Be("zone-hero-name");
        textarea.GetAttribute("aria-describedby").Should().Be("zone-hero-help");
    }

    private IRenderedComponent<PlainZoneEditor> Render(
        string fieldTypeKey,
        string value,
        Action<string> onChanged,
        bool disabled = false)
    {
        var zone = new CapturedSlot(
            "hero", "Hero", fieldTypeKey, IsRequired: false, SortOrder: 0, Configuration: null);

        var context = new FieldEditorContext(
            zone,
            "zone-hero-control",
            "zone-hero-name",
            "zone-hero-help",
            disabled,
            ZoneSeverity.None);

        return _bunit.Render<PlainZoneEditor>(parameters => parameters
            .Add(p => p.Field, context)
            .Add(p => p.Value, value)
            .Add(p => p.ValueChanged, onChanged));
    }
}
