using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Body of <c>POST /api/cms/v1/templates/{id}/zones</c>.
/// </summary>
/// <param name="Key">
/// Stable key the payload addresses this zone by. Chosen once and never changed (spec section 8.5).
/// </param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="FieldTypeKey">Key of the registered field type that fills the zone.</param>
/// <param name="Configuration">
/// Field-type-specific configuration, sent as an object rather than as an escaped string, and
/// checked against that field type's schema before it is stored (spec section 7.2). An empty object
/// is stored as no configuration at all.
/// </param>
/// <param name="Description">Optional help text shown beneath the editor control.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing. Never blocks a draft save.</param>
/// <param name="IsInlineEditable">Whether the zone participates in in-context editing (v2).</param>
/// <param name="Group">Optional tab or accordion grouping in the editor.</param>
/// <param name="SortOrder">Order the zone appears in the editor.</param>
public sealed record CreateZoneRequest(
    string? Key,
    string? Name,
    string? FieldTypeKey,
    JsonElement? Configuration = null,
    string? Description = null,
    bool IsRequired = false,
    bool IsInlineEditable = false,
    string? Group = null,
    int SortOrder = 0);
