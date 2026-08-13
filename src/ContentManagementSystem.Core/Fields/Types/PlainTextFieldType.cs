using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A single line of text with no markup — headings, labels, short titles (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "plainText", "value": "Ship faster" }</c>.
/// <para>
/// No HTML is permitted, and none is stripped either: the value is stored exactly as authored and
/// HTML-encoded when rendered. Sanitizing here instead would mean an author who legitimately types
/// <c>&lt;3</c> or <c>a &lt; b</c> silently loses characters, and it would leave the encoding
/// obligation looking optional at the renderer, where it is the only thing standing between a
/// stored angle bracket and an injected element.
/// </para>
/// </remarks>
public sealed class PlainTextFieldType : TextFieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.PlainText;

    /// <inheritdoc />
    public override string DisplayName => "Text";

    /// <inheritdoc />
    protected override bool AllowsLineBreaks => false;
}
