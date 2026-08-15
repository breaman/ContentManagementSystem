using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// The three steps between a stored value and the renderer that reads it, shared by the two places
/// a value is rendered from: a template's zone and a block's property.
/// </summary>
/// <remarks>
/// A zone and a block property are the same thing at render time exactly as they are at validation
/// time, so the rule about which renderer reads a value — and which configuration it may see — is
/// written once. Two copies would diverge, and the copy that diverged would be the one that handed
/// a renderer settings belonging to another field type.
/// <para>
/// Logging is deliberately left to the caller. The two sites report the same conditions in different
/// words ("zone 'hero'" against "property 'image' of block …"), and an operator reading the log needs
/// to know which of the two it was.
/// </para>
/// </remarks>
internal static class FieldValueDispatch
{
    /// <summary>Reads the field type a stored value declares it was written by.</summary>
    /// <param name="value">The stored property object.</param>
    /// <param name="fieldTypeKey">The key, when the value carries one.</param>
    /// <returns><see langword="true"/> when the value names a field type.</returns>
    /// <remarks>
    /// Never the schema's key. A value has to be read by whatever wrote it, so a property whose
    /// field type was changed under stored content still renders through the renderer that can read
    /// the bytes that are actually there.
    /// </remarks>
    public static bool TryGetFieldTypeKey(JsonElement value, out string fieldTypeKey)
    {
        fieldTypeKey = string.Empty;

        if (value.ValueKind is not JsonValueKind.Object ||
            !value.TryGetProperty(ContentPayloadMembers.Type, out var type) ||
            type.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        fieldTypeKey = type.GetString() ?? string.Empty;

        return fieldTypeKey.Length > 0;
    }

    /// <summary>
    /// The configuration captured for a slot, but only when the schema agrees with the value about
    /// what field type it is.
    /// </summary>
    /// <param name="slot">The captured schema slot, or null when the revision could not be resolved.</param>
    /// <param name="fieldTypeKey">The field type the stored value declares.</param>
    /// <returns>The captured configuration, or <see cref="FieldConfiguration.Empty"/>.</returns>
    /// <remarks>
    /// Handing a renderer the configuration of a different field type is worse than handing it none:
    /// the settings parse, and the value renders under bounds and formats nobody chose for it.
    /// </remarks>
    public static FieldConfiguration Configuration(ContentPropertySchema? slot, string fieldTypeKey) =>
        slot is not null && string.Equals(slot.FieldTypeKey, fieldTypeKey, StringComparison.Ordinal)
            ? slot.Configuration
            : FieldConfiguration.Empty;

    /// <summary>Builds the parameter set every field renderer is handed.</summary>
    /// <param name="value">The stored property object, discriminator included.</param>
    /// <param name="propertyKey">The zone key or block property key the value is stored under.</param>
    /// <param name="configuration">The configuration from <see cref="Configuration"/>.</param>
    /// <returns>Parameters for <c>DynamicComponent</c>.</returns>
    public static Dictionary<string, object?> Parameters(
        JsonElement value,
        string propertyKey,
        FieldConfiguration configuration) =>
        new(StringComparer.Ordinal)
        {
            [nameof(CmsFieldRendererBase.Value)] = value,
            [nameof(CmsFieldRendererBase.PropertyKey)] = propertyKey,
            [nameof(CmsFieldRendererBase.Configuration)] = configuration,
        };
}
