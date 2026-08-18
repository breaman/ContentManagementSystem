using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// The field type to editor mapping (ADR-0014, tasks P6-06 to P6-15).
/// </summary>
/// <remarks>
/// The mirror of <c>FieldRendererCatalogTests</c>, and it exists for the sharper of the two reasons:
/// a field type with no renderer costs a reader a paragraph, while a field type with no editor
/// leaves an author unable to fill a property their template marks required.
/// </remarks>
public class FieldEditorCatalogTests
{
    [Test]
    public void EveryFieldTypeShippedWithTheCmsHasAnEditor()
    {
        var registered = typeof(FieldTypeKeys)
            .GetFields()
            .Where(field => field is { IsLiteral: true, FieldType.Name: nameof(String) })
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToArray();

        var catalog = FieldEditorCatalog.For(registered);

        catalog.FieldTypesWithNoEditor.Should().BeEmpty(
            "an author with no control for a required property cannot publish the page at all");
    }

    [Test]
    public void AFieldTypeWithNoEditorIsReportedRatherThanHidden()
    {
        var catalog = FieldEditorCatalog.For("nobodysFieldType", FieldTypeKeys.PlainText);

        catalog.FieldTypesWithNoEditor.Should().ContainSingle().Which.Should().Be("nobodysFieldType");
    }

    [Test]
    public void AFieldTypeWithNoEditorStillDrawsSomething()
    {
        var catalog = new FieldEditorCatalog();

        catalog.TryGetEditor("nobodysFieldType", out _).Should().BeFalse();

        // An empty card is indistinguishable from an empty value, so the fallback shows what is
        // stored — which is also R13's plain UI if Phase 6 is cut back.
        catalog.EditorFor("nobodysFieldType").Should().Be<PlainZoneEditor>();
    }

    [Test]
    public void AnEditorThatIsNotAComponentIsRefusedWhileTheCatalogIsBeingBuilt()
    {
        var build = () => FieldEditorCatalog.For(
            new Dictionary<string, Type> { ["odd"] = typeof(FieldEditorCatalogTests) },
            ["odd"]);

        // At construction rather than at render: rendering it would fail one zone at a time, in
        // production, on whichever page first reached content using that field type.
        build.Should().Throw<InvalidOperationException>().WithMessage("*not a Razor component*");
    }

    [Test]
    public void EveryEditorTakesTheThreeParametersTheHostPassesByName()
    {
        foreach (var (key, editor) in BuiltInFieldEditors.ByFieldTypeKey)
        {
            // The host dispatches through DynamicComponent, which matches parameters by name. An
            // editor that spelled one differently would throw at render with a message about an
            // unrecognised parameter rather than here with one about the contract.
            typeof(FieldEditorBase).IsAssignableFrom(editor).Should().BeTrue(
                $"the editor for '{key}' has to carry Field, Value, and ValueChanged");

            typeof(IComponent).IsAssignableFrom(editor).Should().BeTrue();
        }
    }
}
