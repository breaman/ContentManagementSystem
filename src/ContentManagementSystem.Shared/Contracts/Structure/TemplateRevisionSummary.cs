namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One entry in a template's structural revision history.
/// </summary>
/// <param name="RevisionNumber">Monotonic number, starting at 1, unique within the template.</param>
/// <param name="IsCurrent">Whether this is the revision new pages capture.</param>
/// <param name="ZoneCount">How many zones the revision captured.</param>
/// <param name="CreatedOn">When the revision was cut.</param>
/// <param name="CreatedBy">Identity of the user whose change cut it.</param>
/// <param name="Notes">Optional note explaining what changed.</param>
/// <remarks>
/// Revisions are addressed by number rather than by database identity, because that is what a page
/// version stores and therefore what a developer reading a page's captured revision has in hand.
/// </remarks>
public sealed record TemplateRevisionSummary(
    int RevisionNumber,
    bool IsCurrent,
    int ZoneCount,
    DateTimeOffset? CreatedOn,
    int CreatedBy,
    string? Notes);
