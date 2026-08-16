using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>color</c> editor — a palette when the slot configures one, a picker when it does not
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// The field type stores exactly one form, <c>#RRGGBB</c>, and refuses named colours, <c>rgb()</c>,
/// and the three-digit shorthand. This control therefore normalises what it is given rather than
/// passing it through: a native colour input already emits lowercase six-digit hex, and what an
/// author types into the hex box is expanded from the shorthand where it can be and refused where it
/// cannot.
/// <para>
/// A configured palette replaces the picker rather than sitting beside it. The palette is a content
/// constraint — it is what stops a brand refresh having to hunt down one-off colours typed into
/// pages over two years — so offering a free picker next to it would only produce values the publish
/// check then rejects.
/// </para>
/// </remarks>
public partial class ColorEditor : FieldEditorBase
{
    /// <summary>The colours the slot allows, empty when it accepts any.</summary>
    private IReadOnlyList<string> Palette => ConfiguredTextList(FieldSettingNames.Palette);

    /// <summary>The stored colour, or empty when nothing is authored.</summary>
    private string Chosen => StoredValue.ReadText(Value) ?? string.Empty;

    private string HexId => $"{Field.ControlId}-hex";

    private string FormatId => $"{Field.ControlId}-format";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, Palette.Count > 0 ? null : FormatId }
            .Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>Whether the stored colour is this swatch, whatever case either was written in.</summary>
    private bool Matches(string swatch) =>
        string.Equals(Chosen, swatch, StringComparison.OrdinalIgnoreCase);

    private string SwatchId(string swatch) => $"{Field.ControlId}-{swatch.TrimStart('#')}";

    private Task OnPickedAsync(ChangeEventArgs args) => WriteColorAsync(args.Value?.ToString());

    private Task OnTypedAsync(ChangeEventArgs args) => WriteColorAsync(args.Value?.ToString());

    /// <summary>
    /// Stores a colour in the one form the field type accepts.
    /// </summary>
    /// <param name="colour">What the control produced, in whatever form.</param>
    /// <remarks>
    /// Anything unrecognisable is stored as typed rather than dropped. The publish check names the
    /// offending property and says what shape it wanted, which is more use to an author than a box
    /// that silently empties itself as they type the fourth character of a six-digit value.
    /// </remarks>
    private Task WriteColorAsync(string? colour)
    {
        if (string.IsNullOrWhiteSpace(colour)) return WriteAsync(string.Empty);

        var text = colour.Trim();

        if (!text.StartsWith('#')) text = "#" + text;

        // #abc is a form browsers accept and the field type does not, and expanding it is what an
        // author who typed it meant. Every other length is left alone for the check to report.
        if (text.Length == 4)
        {
            text = $"#{text[1]}{text[1]}{text[2]}{text[2]}{text[3]}{text[3]}";
        }

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, JsonValue.Create(text.ToLowerInvariant())));
    }
}
