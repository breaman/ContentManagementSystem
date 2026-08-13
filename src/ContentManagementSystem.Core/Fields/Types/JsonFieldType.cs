using System.Text;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Arbitrary JSON — the escape hatch for structured data no other field type models
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "json", "value": … }</c>, where the value is any JSON at all.
/// Configuration keys: <c>required</c>, <c>maxBytes</c>.
/// <para>
/// Flagged <see cref="FieldTypeCapabilities.DeveloperOnly"/>. It is an escape hatch by design and
/// therefore the field type with the fewest guarantees: nothing here contributes to search, and
/// nothing here is a reference. Data that ought to point at a page or an image belongs in a
/// <c>pageReference</c> or a <c>media</c> property — an id buried in a JSON blob is invisible to
/// where-used and to cache invalidation, which is exactly the stale-content failure spec section 7.3
/// is about. That is a limit worth stating rather than a gap worth closing: guessing at which
/// numbers in someone's arbitrary JSON are page ids would be worse than not looking.
/// </para>
/// </remarks>
public sealed class JsonFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Json;

    /// <inheritdoc />
    public override string DisplayName => "JSON";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.DeveloperOnly;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        // Any JSON is by definition valid here; the value arrived as part of a parsed payload, so
        // well-formedness was settled before this call. Size is the only thing left to police.
        if (configuration.GetInt32("maxBytes") is not { } maxBytes) return ValidationResult.Success;

        var bytes = Encoding.UTF8.GetByteCount(value.GetRawText());

        return bytes <= maxBytes
            ? ValidationResult.Success
            : ValidationResult.Error(
                FieldValidationCodes.JsonMaxBytes,
                $"This value is {bytes} bytes; the limit is {maxBytes}.",
                ValueMember);
    }
}
