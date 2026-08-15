using System.Text.Json;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>number</c> value (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>Emitted as stored, with no formatting applied.</strong> Group separators, currency
/// symbols, and a fixed number of decimal places are all presentation decisions that belong to the
/// markup placing the number — a price, a rating, and a floor count want three different answers,
/// and the field type declares no setting that could carry the choice. Formatting here would also
/// make the rendered page depend on the server's culture, so the same content would read
/// differently on two machines in the same cluster.
/// <para>
/// Taking the raw JSON text rather than round-tripping through <see cref="decimal"/> keeps the
/// author's precision: a stored <c>12.50</c> renders as <c>12.50</c> and not as <c>12.5</c>.
/// </para>
/// </remarks>
public partial class NumberRenderer : CmsFieldRendererBase
{
    /// <summary>The stored number as written; empty when the value is absent or is not a number.</summary>
    protected string Text =>
        Member(ValueMember) is { ValueKind: JsonValueKind.Number } number
            ? number.GetRawText()
            : string.Empty;
}
