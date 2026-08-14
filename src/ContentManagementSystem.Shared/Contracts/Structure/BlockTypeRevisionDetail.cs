using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One entry in a block type's structural revision history.
/// </summary>
/// <param name="RevisionNumber">Monotonic number, starting at 1, unique within the block type.</param>
/// <param name="IsCurrent">Whether this is the revision new blocks capture.</param>
/// <param name="PropertyCount">How many properties the revision captured, composed ones included.</param>
/// <param name="CreatedOn">When the revision was cut.</param>
/// <param name="CreatedBy">Identity of the user whose change cut it.</param>
/// <param name="Notes">Optional note explaining what changed.</param>
/// <remarks>
/// Addressed by number rather than by database identity, for the reason
/// <see cref="TemplateRevisionSummary"/> gives: that is what a block instance stores.
/// </remarks>
public sealed record BlockTypeRevisionSummary(
    int RevisionNumber,
    bool IsCurrent,
    int PropertyCount,
    DateTimeOffset? CreatedOn,
    int CreatedBy,
    string? Notes);

/// <summary>
/// One revision together with the property definitions it captured.
/// </summary>
/// <param name="Revision">The history entry.</param>
/// <param name="BlockTypeKey">Key of the block type the revision belongs to.</param>
/// <param name="Properties">
/// The captured snapshot, verbatim, with composed properties already flattened into it — a block
/// instance is rendered from this array alone, so a composition changed or detached afterwards
/// cannot alter what an already-published block shows.
/// </param>
public sealed record BlockTypeRevisionDetail(
    BlockTypeRevisionSummary Revision,
    string BlockTypeKey,
    JsonElement Properties);
