using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// Body of <c>POST /api/cms/v1/block-types</c>.
/// </summary>
/// <param name="Key">Stable key for the new block type. Chosen once and never changed.</param>
/// <param name="Name">Editor-facing name, shown in the block picker.</param>
/// <param name="Description">Optional help text describing when to reach for this block.</param>
/// <param name="IconKey">Icon shown against this block type in the picker.</param>
/// <param name="SummaryTemplate">
/// Token pattern producing a collapsed block's one-line summary, such as <c>{headline}</c>.
/// </param>
/// <remarks>
/// <c>ComponentTypeName</c>, <c>IsOrphaned</c>, and <c>IsBuiltIn</c> are not settable, for the reason
/// <see cref="CreateTemplateRequest"/> gives: the first two are findings of the startup reconciler,
/// and a client that could set the third could make an ordinary block type undeletable.
/// </remarks>
public sealed record CreateBlockTypeRequest(
    string? Key,
    string? Name,
    string? Description = null,
    string? IconKey = null,
    string? SummaryTemplate = null);

/// <summary>
/// Body of <c>PUT /api/cms/v1/block-types/{id}</c>.
/// </summary>
/// <param name="Key">The block type's key. Must equal the stored key.</param>
/// <param name="Name">Editor-facing name. Free to change at any time.</param>
/// <param name="Description">Optional help text.</param>
/// <param name="IconKey">Icon shown in the picker.</param>
/// <param name="SummaryTemplate">Token pattern for a collapsed block's summary.</param>
/// <remarks>
/// Editor-facing metadata only, and therefore allowed even on a built-in block type: renaming
/// "Raw HTML" changes nothing about the shape the renderer expects.
/// </remarks>
public sealed record UpdateBlockTypeRequest(
    string? Key,
    string? Name,
    string? Description = null,
    string? IconKey = null,
    string? SummaryTemplate = null);

/// <summary>
/// Body of <c>POST /api/cms/v1/block-types/{id}/properties</c> and the composition equivalent.
/// </summary>
/// <param name="Key">Stable key the block instance addresses this value by.</param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="FieldTypeKey">Key of the registered field type that fills the property.</param>
/// <param name="Configuration">Field-type-specific configuration, sent as an object.</param>
/// <param name="Description">Optional help text shown beneath the editor control.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="Group">Optional tab or accordion grouping in the editor.</param>
/// <param name="SortOrder">Order the property appears in the editor.</param>
public sealed record CreatePropertyRequest(
    string? Key,
    string? Name,
    string? FieldTypeKey,
    JsonElement? Configuration = null,
    string? Description = null,
    bool IsRequired = false,
    string? Group = null,
    int SortOrder = 0);

/// <summary>
/// Body of <c>PUT</c> on a block-type or composition property.
/// </summary>
/// <param name="Key">The property's key. Must equal the stored key.</param>
/// <param name="Name">Editor-facing label. Free to change.</param>
/// <param name="FieldTypeKey">The property's field type. Must equal the stored key.</param>
/// <param name="Configuration">Field-type configuration, replaced in full rather than patched.</param>
/// <param name="Description">Optional help text.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="Group">Optional editor grouping.</param>
/// <param name="SortOrder">Order in the editor.</param>
public sealed record UpdatePropertyRequest(
    string? Key,
    string? Name,
    string? FieldTypeKey,
    JsonElement? Configuration = null,
    string? Description = null,
    bool IsRequired = false,
    string? Group = null,
    int SortOrder = 0);

/// <summary>
/// Body of <c>POST /api/cms/v1/block-types/{id}/compositions</c>.
/// </summary>
/// <param name="CompositionId">Identity of the composition to compose in.</param>
/// <param name="SortOrder">Order this group appears relative to other composed groups.</param>
public sealed record AttachCompositionRequest(int CompositionId, int SortOrder = 0);
