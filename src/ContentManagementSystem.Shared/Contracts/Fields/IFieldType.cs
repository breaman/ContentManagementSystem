using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// The unit of extensibility in the content model: a storage contract, an editor component, and a
/// renderer component (spec section 7).
/// </summary>
/// <remarks>
/// The payload engine, the publishing pipeline, the cache layer, and the search indexer all dispatch
/// through this interface, which is why adding a field type needs no change to any of them. Register
/// one with <c>services.AddCmsFieldType&lt;T&gt;()</c> and editor placement, publish validation,
/// where-used, cache-tag derivation, search indexing, and version diffing all follow for free.
/// <para>
/// Implementations must be thread-safe and stateless. They are registered as singletons and called
/// concurrently from every request that saves or publishes content.
/// </para>
/// <para>
/// An unknown field type key is never an exception on the public surface: delivery renders nothing
/// and logs a warning (spec section 15.3).
/// </para>
/// </remarks>
public interface IFieldType
{
    /// <summary>
    /// Stable key stored in every payload written by this field type, such as <c>richText</c>.
    /// </summary>
    /// <remarks>Immutable once any content references it. Renaming orphans stored values.</remarks>
    string Key { get; }

    /// <summary>Editor-facing name shown when a developer picks a field type for a zone.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Blazor component rendered in the backoffice to author a value, or null when the component is
    /// supplied by the hosting layer.
    /// </summary>
    /// <remarks>
    /// Null is the normal answer for a field type that lives below the Blazor projects in the
    /// reference graph, as every built-in one does: <c>Core</c> cannot name a type in
    /// <c>Rendering</c> or <c>Client</c>, because both reference it and not the other way round. The
    /// component for those is registered against the key in the field component catalog instead.
    /// A field type shipped in an assembly that does reference the component projects can answer
    /// here directly, which is the simpler path for an extension author (ADR 0014).
    /// </remarks>
    Type? EditorComponent { get; }

    /// <summary>
    /// Blazor component rendered on the public site under static server rendering, or null when the
    /// component is supplied by the hosting layer.
    /// </summary>
    /// <remarks>
    /// See <see cref="EditorComponent"/> for why null is the normal answer. A field type with no
    /// renderer from either source renders nothing and logs a warning, exactly as an unknown field
    /// type key does (spec section 15.3) — a missing component is never an exception on the public
    /// surface.
    /// </remarks>
    Type? RendererComponent { get; }

    /// <summary>What this field type can do, so the surrounding engine need not special-case its key.</summary>
    FieldTypeCapabilities Capabilities { get; }

    /// <summary>
    /// Checks one stored value against its configuration.
    /// </summary>
    /// <param name="value">
    /// The value as stored in the payload. Kept as a <see cref="JsonElement"/> deliberately —
    /// binding to a CLR model here is where the absent-versus-null distinction goes to die
    /// (spec section 6.2).
    /// </param>
    /// <param name="configuration">Parsed configuration for the zone or property being validated.</param>
    /// <param name="mode">Whether a draft is being saved or content is being published.</param>
    /// <param name="cancellationToken">Token observed while validating.</param>
    /// <returns>Diagnostics found, each carrying a stable code.</returns>
    ValueTask<ValidationResult> ValidateAsync(
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the value cleaned of anything unsafe to store.
    /// </summary>
    /// <param name="value">The value as submitted.</param>
    /// <param name="configuration">Parsed configuration, which may select a sanitization profile.</param>
    /// <param name="cancellationToken">Token observed while sanitizing.</param>
    /// <returns>The value to persist. Field types with no markup return it unchanged.</returns>
    /// <remarks>
    /// Sanitizing on write does not excuse the renderer from sanitizing again on read
    /// (ADR 0008): stored content predates every change to the allowlist, and only the render-time
    /// pass covers rows written before today's rules existed.
    /// </remarks>
    ValueTask<JsonElement> SanitizeAsync(
        JsonElement value,
        FieldConfiguration configuration,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reports every entity this value points at.
    /// </summary>
    /// <param name="value">The value as stored in the payload.</param>
    /// <returns>
    /// One <see cref="ContentReference"/> per referenced entity, with <c>Path</c> left null for the
    /// schema walk to fill in.
    /// </returns>
    /// <remarks>
    /// The one rule that matters in this interface. A field type that omits a reference produces a
    /// page that silently fails to invalidate when its dependency changes; the symptom is stale
    /// content and it is hard to trace back (spec section 7.3).
    /// <para>
    /// A field type holding values of other field types — anything flagged
    /// <see cref="FieldTypeCapabilities.Container"/> — must delegate back into the schema walk
    /// rather than reading its contents itself, or every reference nested inside it is dropped.
    /// </para>
    /// </remarks>
    IEnumerable<ContentReference> ExtractReferences(JsonElement value);

    /// <summary>
    /// Reduces the value to plain text for the search index.
    /// </summary>
    /// <param name="value">The value as stored in the payload.</param>
    /// <returns>
    /// Indexable text, or an empty string for field types that contribute nothing searchable.
    /// Never null.
    /// </returns>
    string ExtractSearchText(JsonElement value);
}
