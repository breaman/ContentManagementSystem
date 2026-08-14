using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Reads and writes the zone definitions of one template (task P1-22).
/// </summary>
/// <remarks>
/// Every method authorizes the caller itself, for the reason <see cref="ITemplateService"/> does:
/// the endpoint policy is the door, this is the lock, and a CLI verb or the schema sync reaching the
/// same code is subject to the same rules (spec section 20.4).
/// <para>
/// The evolution rules of spec section 8.5 are enforced here, and they are the whole point of the
/// type. Adding a zone is free. Removing one is allowed and keeps the payload data. Renaming a key
/// is refused. Changing a field type is refused until the converter machinery exists to rewrite the
/// values already stored under it.
/// </para>
/// <para>
/// A change that alters how stored content is read cuts a new <c>TemplateRevision</c>; a change to a
/// label does not. Published content renders against the revision it captured, so cutting one is how
/// a structural change is made safe rather than merely recorded.
/// </para>
/// </remarks>
public interface IZoneService
{
    /// <summary>Lists a template's zones in editor order.</summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The zone definitions, or a not-found result when the template does not exist.</returns>
    Task<StructureResult<IReadOnlyList<ZoneDefinition>>> ListAsync(
        int templateId,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one zone.</summary>
    /// <param name="templateId">Identity of the template the zone belongs to.</param>
    /// <param name="zoneId">Identity of the zone.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The zone definition, or a not-found result.</returns>
    Task<StructureResult<ZoneDefinition>> GetAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a zone to a template and cuts a new revision.
    /// </summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="request">The zone to add.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The stored zone with any non-blocking warnings, an invalid result naming every rule the
    /// request broke, a conflict when the key is taken within the template, or a not-found result.
    /// </returns>
    /// <remarks>
    /// Adding a zone never invalidates existing content: pages authored before it read the new key
    /// as absent. A required zone added to a template with live pages fails those pages only on
    /// their next publish, which is the behaviour spec section 8.5 asks for.
    /// </remarks>
    Task<StructureResult<ZoneSaveResult>> CreateAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a zone, cutting a revision only if what changed affects how content is read.
    /// </summary>
    /// <param name="templateId">Identity of the template the zone belongs to.</param>
    /// <param name="zoneId">Identity of the zone.</param>
    /// <param name="request">The new values. A changed key or field type is refused.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The stored zone with any warnings, an invalid result, or a not-found result.</returns>
    Task<StructureResult<ZoneSaveResult>> UpdateAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a zone definition and cuts a new revision.
    /// </summary>
    /// <param name="templateId">Identity of the template the zone belongs to.</param>
    /// <param name="zoneId">Identity of the zone.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What was removed and the resulting revision, or a not-found result.</returns>
    /// <remarks>
    /// Unguarded on purpose. Content stored under the key survives untouched — the payload is not
    /// rewritten, the schema walk reports the leftover value as orphaned rather than invalid, and an
    /// editor recovers or discards it deliberately (spec section 8.5). Blocking the removal while
    /// content exists would be the stricter-looking choice and the wrong one: it would make a
    /// content model unchangeable the moment anybody used it.
    /// </remarks>
    Task<StructureResult<ZoneRemovalResult>> DeleteAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default);
}
