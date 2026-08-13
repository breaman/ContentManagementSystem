using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Extracts every entity a payload points at, for projection into <c>ContentReference</c> rows.
/// </summary>
/// <remarks>
/// The relational half of the storage decision in spec section 6.2. The payload answers "what does
/// this page contain"; these rows answer "which pages contain this", which is what makes where-used,
/// link integrity, cache-tag derivation, and orphan detection indexed queries rather than a scan over
/// JSON. They are rewritten on every save and publish.
/// <para>
/// The failure this exists to prevent is under-reporting. A reference that is missed produces a page
/// that silently fails to invalidate when its dependency changes, and stale content is close to
/// untraceable after the fact (spec section 7.3).
/// </para>
/// </remarks>
public interface IReferenceIndexer
{
    /// <summary>
    /// Walks a payload and reports every reference it holds.
    /// </summary>
    /// <param name="payload">The payload to walk.</param>
    /// <returns>
    /// One entry per reference occurrence, in document order, each carrying the absolute payload path
    /// it was found at. The same target referenced twice yields two entries with different paths;
    /// collapsing them is the projection's business, and knowing both places is what lets the
    /// backoffice show an editor where a reference actually is.
    /// </returns>
    IReadOnlyList<ContentReference> Extract(ContentPayload payload);
}

/// <summary>
/// The default <see cref="IReferenceIndexer"/>, dispatching by the field type that wrote each value.
/// </summary>
/// <remarks>
/// <strong>This walk is driven by the payload, not by the schema</strong>, which is the one design
/// decision here worth stating outright. Dispatching by the stored <c>type</c> discriminator rather
/// than by the zone's declared field type follows the same rule the container field types already
/// obey — a value has to be read by whatever wrote it — but it also makes the indexer robust in the
/// two cases where a schema-driven walk quietly returns nothing:
/// <list type="bullet">
/// <item><description>
/// the template revision the payload captured is no longer known, in which case a schema-driven walk
/// has no zones to iterate and would <em>erase</em> the page's reference rows on its next save;
/// </description></item>
/// <item><description>
/// the zone has since been removed from the template, in which case its content is retained as
/// orphaned (spec section 8.5) and the media it points at is still, in a real sense, in use.
/// </description></item>
/// </list>
/// Both make the index over-report rather than under-report, which is the right direction: an extra
/// row makes a delete guard slightly more cautious, while a missing one makes a page go stale.
/// </remarks>
public sealed class ReferenceIndexer : IReferenceIndexer
{
    private readonly IFieldTypeRegistry _registry;

    /// <summary>
    /// Creates the indexer.
    /// </summary>
    /// <param name="registry">The field types this deployment knows about.</param>
    public ReferenceIndexer(IFieldTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<ContentReference> Extract(ContentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.HasZones) return [];

        List<ContentReference>? references = null;

        foreach (var zone in payload.Zones.EnumerateObject())
        {
            if (ReadTypeKey(zone.Value) is not { } typeKey) continue;

            // A key nothing is registered under is skipped rather than thrown on: content outlives
            // the code that was deployed when it was written (spec section 15.3). Its references are
            // lost until the field type is restored, which is unavoidable — nothing else can read
            // the value's shape.
            if (_registry.Find(typeKey) is not { } fieldType) continue;

            if (!fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing)) continue;

            var zonePath = $"{ContentPayloadMembers.Zones}.{zone.Name}";

            foreach (var reference in fieldType.ExtractReferences(zone.Value))
            {
                // The field type reported a path relative to its own value — null for the value as a
                // whole, 'items[1].properties.image' for something nested inside it. Prefixing the
                // zone is the only part it could not know.
                (references ??= []).Add(reference with
                {
                    Path = Combine(zonePath, reference.Path),
                });
            }
        }

        return references is null ? [] : references;
    }

    private static string Combine(string prefix, string? relative) =>
        string.IsNullOrEmpty(relative)
            ? prefix
            : relative[0] is '['
                ? prefix + relative
                : $"{prefix}.{relative}";

    private static string? ReadTypeKey(JsonElement value) =>
        value.ValueKind is JsonValueKind.Object &&
        value.TryGetProperty(ContentPayloadMembers.Type, out var type) &&
        type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;
}
