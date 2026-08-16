using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>boolean</c> editor — a switch, with a word beside it (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>Three states, drawn as two.</strong> A boolean slot can be absent, true, or false, and
/// the field type is explicit that <c>false</c> is a <em>filled</em> value: a required boolean is
/// satisfied by an author deliberately turning something off. A switch has only two positions, so
/// the third state — never touched — is said in words underneath rather than mimed by a half-drawn
/// control nobody would recognise.
/// <para>
/// The distinction is not pedantry. Absent means the renderer uses its fallback and <c>false</c>
/// means the author declined it, and a control that wrote <c>false</c> for a switch simply never
/// touched would silently overrule every template default on the page.
/// </para>
/// </remarks>
public partial class BooleanEditor : FieldEditorBase
{
    /// <summary>The stored value, or null when the slot has never been authored.</summary>
    private bool? Stored => StoredValue.ReadBoolean(Value);

    /// <summary>Whether the switch is drawn on.</summary>
    private bool IsOn => Stored is true;

    /// <summary>Whether nothing has been chosen either way.</summary>
    private bool IsUnset => Stored is null;

    /// <summary>What to call the on position, taken from the slot's own name where it reads better.</summary>
    private static string OnLabel => "On";

    /// <summary>What to call the off position.</summary>
    private static string OffLabel => "Off";

    private string UnsetId => $"{Field.ControlId}-unset";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, IsUnset ? UnsetId : null }.Where(id => !string.IsNullOrEmpty(id)));

    /// <summary>Stores the chosen position, which is a value either way.</summary>
    private Task OnChangedAsync(ChangeEventArgs args) =>
        WriteAsync(StoredValue.Write(Value, FieldTypeKey, JsonValue.Create((bool?)args.Value ?? false)));
}
