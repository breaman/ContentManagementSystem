using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Rewrites the entities a whole payload points at (spec section 14.12).
/// </summary>
/// <remarks>
/// The payload-level counterpart of <see cref="IReferenceIndexer"/>, and driven the same way: by the
/// payload rather than by the schema, dispatching on each zone's stored <c>type</c> discriminator.
/// A schema-driven walk would silently skip a zone whose definition has since been removed, and the
/// links inside retained content are exactly as wrong as the ones inside live content when a copy
/// still points at the original.
/// </remarks>
public interface IContentPayloadRemapper
{
    /// <summary>
    /// Applies a remapping to every reference in a payload.
    /// </summary>
    /// <param name="payload">The payload to rewrite.</param>
    /// <param name="remap">Supplies each target's replacement.</param>
    /// <returns>
    /// The rewritten payload, or the original instance when nothing changed. Reference equality is
    /// the signal a caller uses to skip a pointless write.
    /// </returns>
    ContentPayload Remap(ContentPayload payload, ReferenceRemapper remap);
}

/// <inheritdoc cref="IContentPayloadRemapper" />
/// <param name="registry">The field types this deployment knows about.</param>
public sealed class ContentPayloadRemapper(IFieldTypeRegistry registry) : IContentPayloadRemapper
{
    /// <inheritdoc />
    public ContentPayload Remap(ContentPayload payload, ReferenceRemapper remap)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(remap);

        if (!payload.HasZones) return payload;

        ContentPayloadBuilder? builder = null;

        foreach (var zone in payload.Zones.EnumerateObject())
        {
            if (ReadTypeKey(zone.Value) is not { } typeKey) continue;

            // A key nothing is registered under is skipped rather than thrown on, for the reason the
            // indexer gives: content outlives the code that was deployed when it was written.
            if (registry.Find(typeKey) is not { } fieldType) continue;

            if (!fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing) &&
                !fieldType.Capabilities.HasFlag(FieldTypeCapabilities.Container))
            {
                continue;
            }

            if (fieldType.RemapReferences(zone.Value, remap) is not { } rewritten) continue;

            // Built lazily, so a payload with nothing to rewrite is handed back as the same instance
            // and the caller can skip the write entirely.
            builder ??= new ContentPayloadBuilder(payload);
            builder.SetZone(zone.Name, rewritten.ToJsonString());
        }

        return builder?.Build() ?? payload;
    }

    private static string? ReadTypeKey(JsonElement value) =>
        value.ValueKind is JsonValueKind.Object &&
        value.TryGetProperty(ContentPayloadMembers.Type, out var type) &&
        type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;
}
