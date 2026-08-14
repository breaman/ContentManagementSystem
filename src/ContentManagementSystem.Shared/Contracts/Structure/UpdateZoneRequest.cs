using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Body of <c>PUT /api/cms/v1/templates/{id}/zones/{zoneId}</c>.
/// </summary>
/// <param name="Key">
/// The zone's key. Must equal the stored key: a change is refused with
/// <see cref="StructureCodes.KeyImmutable"/>.
/// </param>
/// <param name="Name">Editor-facing label. Free to change at any time.</param>
/// <param name="FieldTypeKey">
/// The zone's field type. Must equal the stored key: changing what a zone holds is a content
/// migration, not an edit, and is refused with <see cref="StructureCodes.FieldTypeImmutable"/>.
/// </param>
/// <param name="Configuration">
/// Field-type-specific configuration. Sent in full, not patched — an omitted configuration clears
/// the stored one, because a partial merge would leave a developer no way to remove a setting.
/// </param>
/// <param name="Description">Optional help text shown beneath the editor control.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="IsInlineEditable">Whether the zone participates in in-context editing (v2).</param>
/// <param name="Group">Optional tab or accordion grouping in the editor.</param>
/// <param name="SortOrder">Order the zone appears in the editor.</param>
/// <remarks>
/// The key and the field type are both carried even though neither can change, for the reason
/// <see cref="UpdateTemplateRequest"/> carries its key: an edit form round-trips what it was given,
/// and refusing a change by name beats discarding it silently.
/// </remarks>
public sealed record UpdateZoneRequest(
    string? Key,
    string? Name,
    string? FieldTypeKey,
    JsonElement? Configuration = null,
    string? Description = null,
    bool IsRequired = false,
    bool IsInlineEditable = false,
    string? Group = null,
    int SortOrder = 0);
