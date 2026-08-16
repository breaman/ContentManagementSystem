using Bunit;

using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Tests.Canvas;

/// <summary>
/// The card body the canvas draws until a field type has an editor of its own (task P6-05).
/// </summary>
public class PlainZoneEditorTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ATextZoneRaisesWhatIsTypedIntoIt()
    {
        var raised = string.Empty;

        var editor = Render(FieldTypeKeys.RichText, "before", value => raised = value);

        editor.Find("textarea").Change("after");

        raised.Should().Be("after");
    }

    [Fact]
    public void AFieldTypeWithNoEditorYetIsShownAsStoredAndNotEditable()
    {
        var editor = Render(FieldTypeKeys.Blocks, """{ "items": [] }""", _ => { });

        var textarea = editor.Find("textarea");

        // Read-only rather than absent: inventing a control for a block list would mean inventing a
        // shape for its value, and the first thing P6-06 would have to do is repair what it wrote.
        textarea.HasAttribute("readonly").Should().BeTrue();
        textarea.GetAttribute("value").Should().Contain("items");
        editor.Markup.Should().Contain("P6-06");
    }

    [Fact]
    public void ADisabledZoneCannotBeTypedInto()
    {
        var editor = Render(FieldTypeKeys.PlainText, "value", _ => { }, disabled: true);

        editor.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TheControlIsNamedAndDescribedByTheCardAroundIt()
    {
        var editor = Render(FieldTypeKeys.PlainText, "value", _ => { });

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

        var context = new ZoneEditorContext(
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
