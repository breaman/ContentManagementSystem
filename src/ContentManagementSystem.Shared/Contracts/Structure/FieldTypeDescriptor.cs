using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One registered field type as <c>GET /api/cms/v1/field-types</c> reports it.
/// </summary>
/// <param name="Key">Stable key stored in every payload the field type writes.</param>
/// <param name="DisplayName">Editor-facing name, shown when picking a field type for a zone.</param>
/// <param name="Capabilities">
/// The capability flags by name, such as <c>Searchable</c> or <c>ReferenceBearing</c>. Sent as names
/// rather than as the enum's numeric value: the flags are a contract with the backoffice, and a
/// client that had to know which bit meant what would break the first time one was inserted.
/// </param>
/// <param name="IsContainer">
/// Whether the field type holds other values, such as <c>blocks</c>. Pulled out of the flags because
/// the structure screen branches on it — a container zone offers an allowed-block-type picker that
/// nothing else does.
/// </param>
/// <param name="IsDeveloperOnly">Whether the field type is hidden from non-developer authors.</param>
/// <param name="ConfigurationSchema">
/// The JSON Schema document describing this field type's configuration, generated from the schema
/// declared in code (ADR 0015). The configuration form builds its controls from this.
/// </param>
/// <remarks>
/// Read-only, and deliberately so. A field type is code: it arrives with a deployment and cannot be
/// created, edited, or removed through the API. What this endpoint exists for is the other half of
/// spec section 7.2 — the client cannot render a configuration form for a field type it knows
/// nothing about, and hard-coding the settings into the backoffice would put the authority in two
/// places and break every third-party field type.
/// </remarks>
public sealed record FieldTypeDescriptor(
    string Key,
    string DisplayName,
    IReadOnlyList<string> Capabilities,
    bool IsContainer,
    bool IsDeveloperOnly,
    JsonElement ConfigurationSchema);
