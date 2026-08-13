namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// A template with the zone definitions an editor will be asked to fill.
/// </summary>
/// <param name="Template">The template itself.</param>
/// <param name="Zones">Zone definitions in editor order.</param>
/// <param name="CreatedOn">When the template was first created.</param>
/// <param name="ModifiedOn">When the template row was last changed.</param>
/// <remarks>
/// <paramref name="Zones"/> describes the template as it stands <em>now</em>. It is not what an
/// existing page validates against — that is the revision the page captured, served by the revision
/// endpoints (spec section 8.5).
/// </remarks>
public sealed record TemplateDetail(
    TemplateSummary Template,
    IReadOnlyList<ZoneDefinition> Zones,
    DateTimeOffset? CreatedOn,
    DateTimeOffset? ModifiedOn);
