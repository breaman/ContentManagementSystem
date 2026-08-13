using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One zone definition as the structure API reports it (spec section 8.3).
/// </summary>
/// <param name="Id">Database identity, used to address the zone in the API.</param>
/// <param name="Key">Stable payload key. Immutable once created.</param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="Description">Optional help text shown beneath the editor control.</param>
/// <param name="FieldTypeKey">Key of the registered field type that fills the zone.</param>
/// <param name="Configuration">
/// Field-type-specific configuration, sent as an object rather than as an escaped string. The
/// configuration form builds its controls from the field type's JSON Schema and binds them to this
/// value; handing it over as text would make every client parse it again to do that.
/// </param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="IsInlineEditable">Whether the zone participates in in-context editing (v2).</param>
/// <param name="Group">Optional tab or accordion grouping in the editor.</param>
/// <param name="SortOrder">Order the zone appears in the editor.</param>
public sealed record ZoneDefinition(
    int Id,
    string Key,
    string Name,
    string? Description,
    string FieldTypeKey,
    JsonElement? Configuration,
    bool IsRequired,
    bool IsInlineEditable,
    string? Group,
    int SortOrder);
