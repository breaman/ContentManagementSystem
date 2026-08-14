using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One block type as the structure list reports it (spec section 6.3).
/// </summary>
/// <param name="Id">Database identity, used to address the block type in the API.</param>
/// <param name="Key">Stable key written into every block instance in a payload.</param>
/// <param name="Name">Editor-facing name, shown in the block picker.</param>
/// <param name="Description">Optional help text describing when to reach for this block.</param>
/// <param name="ComponentTypeName">Razor component that renders it, or null while none is deployed.</param>
/// <param name="IconKey">Icon shown against this block type in the picker.</param>
/// <param name="SummaryTemplate">Token pattern producing a collapsed block's one-line summary.</param>
/// <param name="IsOrphaned">Whether the database holds it but no deployed component declares it.</param>
/// <param name="IsBuiltIn">
/// Whether the system itself depends on this block type. A built-in's property set is fixed: the
/// code that renders it expects exactly that shape, so structural edits are refused.
/// </param>
/// <param name="CurrentRevision">Newest revision number.</param>
/// <param name="PropertyCount">Number of properties, composed ones included.</param>
public sealed record BlockTypeSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    string? ComponentTypeName,
    string? IconKey,
    string? SummaryTemplate,
    bool IsOrphaned,
    bool IsBuiltIn,
    int CurrentRevision,
    int PropertyCount);

/// <summary>
/// One block type with the property set an editor will be shown.
/// </summary>
/// <param name="BlockType">The block type itself.</param>
/// <param name="Properties">Properties declared directly on it, in editor order.</param>
/// <param name="Compositions">Shared groups composed into it, in the order they are appended.</param>
/// <param name="EffectiveProperties">
/// The flattened set — own properties followed by each composed group's — which is what a revision
/// snapshot captures and what an editor actually renders. Sent alongside the parts rather than
/// instead of them, because the structure screen has to show which properties it may edit here and
/// which belong to a composition.
/// </param>
/// <param name="CreatedOn">When the block type was created.</param>
/// <param name="ModifiedOn">When it was last changed.</param>
public sealed record BlockTypeDetail(
    BlockTypeSummary BlockType,
    IReadOnlyList<PropertyDefinition> Properties,
    IReadOnlyList<CompositionBinding> Compositions,
    IReadOnlyList<PropertyDefinition> EffectiveProperties,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ModifiedOn);

/// <summary>
/// One property definition on a block type or a composition (spec section 8.3, which zones share).
/// </summary>
/// <param name="Id">Database identity, used to address the property in the API.</param>
/// <param name="Key">Stable payload key. Immutable once created.</param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="Description">Optional help text shown beneath the editor control.</param>
/// <param name="FieldTypeKey">Key of the registered field type that fills the property.</param>
/// <param name="Configuration">Field-type-specific configuration, sent as an object.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="Group">Optional tab or accordion grouping in the editor.</param>
/// <param name="SortOrder">Order the property appears in the editor.</param>
/// <param name="CompositionKey">
/// Key of the composition this property came from, or null when the block type declares it
/// directly. A composed property is not editable on the host block type — editing it there would
/// silently fork one definition into many — so the client needs to know which is which.
/// </param>
public sealed record PropertyDefinition(
    int Id,
    string Key,
    string Name,
    string? Description,
    string FieldTypeKey,
    JsonElement? Configuration,
    bool IsRequired,
    string? Group,
    int SortOrder,
    string? CompositionKey = null);

/// <summary>
/// A composition composed into a block type.
/// </summary>
/// <param name="CompositionId">Identity of the composition.</param>
/// <param name="Key">Stable key of the composition.</param>
/// <param name="Name">Editor-facing name of the composition.</param>
/// <param name="SortOrder">Order this group's properties appear relative to other composed groups.</param>
/// <param name="PropertyCount">How many properties the group contributes.</param>
public sealed record CompositionBinding(
    int CompositionId,
    string Key,
    string Name,
    int SortOrder,
    int PropertyCount);

/// <summary>What a property create or update produced.</summary>
/// <param name="Property">The property as it now stands.</param>
/// <param name="CurrentRevision">The owner's revision number after the write.</param>
/// <param name="Warnings">Non-blocking diagnostics about what was stored.</param>
/// <remarks>Mirrors <see cref="ZoneSaveResult"/>, for the reasons documented there.</remarks>
public sealed record PropertySaveResult(
    PropertyDefinition Property,
    int CurrentRevision,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>What removing a property produced.</summary>
/// <param name="Key">Key of the removed property, which stored payloads still carry.</param>
/// <param name="CurrentRevision">The owner's revision number after the removal.</param>
public sealed record PropertyRemovalResult(string Key, int CurrentRevision);
