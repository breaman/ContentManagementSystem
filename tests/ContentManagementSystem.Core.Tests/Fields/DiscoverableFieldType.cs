using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields;

/// <summary>
/// A public, container-constructible field type, so the assembly scan has something legitimate to
/// find in this assembly.
/// </summary>
public sealed class DiscoverableFieldType : IFieldType
{
    /// <inheritdoc />
    public string Key => "discoverable";

    /// <inheritdoc />
    public string DisplayName => "Discoverable";

    /// <inheritdoc />
    public Type EditorComponent => typeof(object);

    /// <inheritdoc />
    public Type RendererComponent => typeof(object);

    /// <inheritdoc />
    public FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
    public FieldConfigurationSchema ConfigurationSchema => FieldConfigurationSchema.Empty;

    /// <inheritdoc />
    public ValueTask<ValidationResult> ValidateAsync(
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode,
        CancellationToken cancellationToken) => ValueTask.FromResult(ValidationResult.Success);

    /// <inheritdoc />
    public ValueTask<JsonElement> SanitizeAsync(
        JsonElement value,
        FieldConfiguration configuration,
        CancellationToken cancellationToken) => ValueTask.FromResult(value);

    /// <inheritdoc />
    public IEnumerable<ContentReference> ExtractReferences(JsonElement value) => [];

    /// <inheritdoc />
    public string ExtractSearchText(JsonElement value) => string.Empty;
}
