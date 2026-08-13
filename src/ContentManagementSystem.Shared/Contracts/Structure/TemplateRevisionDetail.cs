using System.Text.Json;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// One revision together with the zone definitions it captured.
/// </summary>
/// <param name="Revision">The history entry.</param>
/// <param name="TemplateKey">Key of the template the revision belongs to.</param>
/// <param name="Zones">
/// The captured snapshot, verbatim: an array of slot definitions in the format
/// <c>ContentSchemaSnapshot</c> writes.
/// </param>
/// <remarks>
/// The snapshot is passed through as stored rather than projected into
/// <see cref="ZoneDefinition"/>. A revision is a historical record — it has no database identities to
/// address and may describe zones, field types, or settings this deployment no longer has. Reshaping
/// it into the live contract would quietly drop whatever no longer fits, which is exactly the
/// information someone reading an old revision is looking for.
/// </remarks>
public sealed record TemplateRevisionDetail(
    TemplateRevisionSummary Revision,
    string TemplateKey,
    JsonElement Zones);
