using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A switch — "show the sidebar", "open links in a new tab" (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "boolean", "value": true }</c>.
/// <para>
/// <c>false</c> is a filled value, not an empty one: a required boolean is satisfied by an author
/// deliberately turning something off, and treating that as unfilled would make the property
/// impossible to publish.
/// </para>
/// </remarks>
public sealed class BooleanFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Boolean;

    /// <inheritdoc />
    public override string DisplayName => "Yes/No";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode) =>
        value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? ValidationResult.Success
            : ValidationResult.Error(FieldValidationCodes.Shape, "Expected true or false.", ValueMember);
}
