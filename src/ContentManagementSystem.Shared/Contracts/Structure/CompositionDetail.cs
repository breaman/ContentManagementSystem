using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One shared property group as the structure list reports it (spec section 6.3).
/// </summary>
/// <param name="Id">Database identity, used to address the composition in the API.</param>
/// <param name="Key">Stable key, such as <c>spacing-options</c>. Immutable once created.</param>
/// <param name="Name">Editor-facing display name.</param>
/// <param name="Description">Optional help text describing what the group is for.</param>
/// <param name="PropertyCount">How many properties the group contributes wherever it is composed.</param>
/// <param name="BlockTypeCount">
/// How many block types compose it. This is the blast radius of any edit to the group, so it belongs
/// in the list rather than one click further in.
/// </param>
public sealed record CompositionSummary(
    int Id,
    string Key,
    string Name,
    string? Description,
    int PropertyCount,
    int BlockTypeCount);

/// <summary>
/// One composition with its properties and the block types composing it.
/// </summary>
/// <param name="Composition">The composition itself.</param>
/// <param name="Properties">Its property definitions, in the order they are contributed.</param>
/// <param name="BlockTypeKeys">
/// Keys of the block types composing it, so a developer can see what an edit here will change before
/// making it. This is where-used for structure, and it is also the guard on deletion.
/// </param>
/// <param name="CreatedOn">When the composition was created.</param>
/// <param name="ModifiedOn">When it was last changed.</param>
public sealed record CompositionDetail(
    CompositionSummary Composition,
    IReadOnlyList<PropertyDefinition> Properties,
    IReadOnlyList<string> BlockTypeKeys,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ModifiedOn);

/// <summary>Body of <c>POST /api/cms/v1/compositions</c>.</summary>
/// <param name="Key">Stable key for the new group. Chosen once and never changed.</param>
/// <param name="Name">Editor-facing display name.</param>
/// <param name="Description">Optional help text.</param>
public sealed record CreateCompositionRequest(string? Key, string? Name, string? Description = null);

/// <summary>Body of <c>PUT /api/cms/v1/compositions/{id}</c>.</summary>
/// <param name="Key">The composition's key. Must equal the stored key.</param>
/// <param name="Name">Editor-facing display name. Free to change.</param>
/// <param name="Description">Optional help text.</param>
public sealed record UpdateCompositionRequest(string? Key, string? Name, string? Description = null);

/// <summary>
/// What a change to a composition's property produced.
/// </summary>
/// <param name="Property">The property as it now stands.</param>
/// <param name="AffectedBlockTypeKeys">
/// Every block type that composes this group, each of which had a new revision cut for it. A
/// composition is not itself revisioned — nothing addresses it from a payload — so this, not a
/// revision number, is what tells a developer how far the edit reached.
/// </param>
/// <param name="Warnings">Non-blocking diagnostics about what was stored.</param>
public sealed record CompositionPropertySaveResult(
    PropertyDefinition Property,
    IReadOnlyList<string> AffectedBlockTypeKeys,
    IReadOnlyList<ApiDiagnostic> Warnings);

/// <summary>What removing a composition property produced.</summary>
/// <param name="Key">Key of the removed property, which stored blocks still carry.</param>
/// <param name="AffectedBlockTypeKeys">Block types that had a new revision cut for them.</param>
public sealed record CompositionPropertyRemovalResult(
    string Key,
    IReadOnlyList<string> AffectedBlockTypeKeys);
