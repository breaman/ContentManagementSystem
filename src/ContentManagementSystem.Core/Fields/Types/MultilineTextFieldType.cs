using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Several lines of text with no markup — summaries, meta descriptions, image captions
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "multilineText", "value": "First line\nSecond line" }</c>.
/// <para>
/// Identical to <see cref="PlainTextFieldType"/> except that line breaks are kept. They are stored
/// as authored and are the renderer's to interpret — turning them into <c>&lt;br&gt;</c> at write
/// time would put markup into a field whose whole contract is that it holds none.
/// </para>
/// </remarks>
public sealed class MultilineTextFieldType : TextFieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.MultilineText;

    /// <inheritdoc />
    public override string DisplayName => "Multi-line text";

    /// <inheritdoc />
    protected override bool AllowsLineBreaks => true;
}
