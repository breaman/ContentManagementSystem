using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Hand-written HTML — embeds, third-party widgets, markup no editor produces (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "html", "value": "&lt;iframe …&gt;" }</c>. Configuration keys:
/// <c>required</c>, <c>maxLength</c>.
/// <para>
/// Flagged <see cref="FieldTypeCapabilities.DeveloperOnly"/>: its value reaches the page more or
/// less as written, so the property is placed on a template by, and authored by, someone holding the
/// <c>Developer</c> role.
/// </para>
/// <para>
/// That restriction widens the allowlist; it does not remove it. The <c>Developer</c> sanitization
/// profile still refuses <c>&lt;script&gt;</c>, event handler attributes, and off-allowlist URL
/// schemes (spec section 20.2) — a role is an authorization decision, and treating it as a reason to
/// store markup unchecked would make every later privilege mistake a stored-XSS incident.
/// </para>
/// </remarks>
public sealed class HtmlFieldType : FieldTypeBase
{
    private readonly IContentSanitizer _sanitizer;

    /// <summary>Creates the field type.</summary>
    /// <param name="sanitizer">Applies the allowlist profile to authored markup.</param>
    public HtmlFieldType(IContentSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        _sanitizer = sanitizer;
    }

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Html;

    /// <inheritdoc />
    public override string DisplayName => "HTML";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities =>
        FieldTypeCapabilities.Searchable |
        FieldTypeCapabilities.Sanitizable |
        FieldTypeCapabilities.DeveloperOnly;

    /// <inheritdoc />
    protected override bool IsEmpty(JsonElement value) =>
        base.IsEmpty(value) ||
        (value.ValueKind is JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        if (value.ValueKind is not JsonValueKind.String)
        {
            return ValidationResult.Error(FieldValidationCodes.Shape, "Expected markup.", ValueMember);
        }

        var markup = value.GetString()!;

        if (configuration.GetInt32("maxLength") is { } maxLength && markup.Length > maxLength)
        {
            return ValidationResult.Error(
                FieldValidationCodes.MaxLength,
                $"Use at most {maxLength} characters; this is {markup.Length}.",
                ValueMember);
        }

        return ValidationResult.Success;
    }

    /// <inheritdoc />
    public override ValueTask<JsonElement> SanitizeAsync(
        JsonElement value,
        FieldConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        if (GetStringValue(value) is not { Length: > 0 } markup)
        {
            return ValueTask.FromResult(value);
        }

        var sanitized = _sanitizer.Sanitize(markup, SanitizationProfile.Developer);

        return ValueTask.FromResult(
            string.Equals(sanitized, markup, StringComparison.Ordinal)
                ? value
                : StoredProperty.WithStringMember(value, ValueMember, sanitized));
    }

    /// <inheritdoc />
    public override string ExtractSearchText(JsonElement value) => SearchText.FromHtml(GetStringValue(value));
}
