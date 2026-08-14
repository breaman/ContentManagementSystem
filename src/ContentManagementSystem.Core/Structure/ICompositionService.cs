using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Reads and writes the shared property groups block types compose (task P1-24).
/// </summary>
/// <remarks>
/// The thing that makes this more than a second copy of the block type service: a composition is
/// <em>not</em> revisioned, because nothing in a payload addresses it. A block instance names its
/// block type and a revision number, and that revision's snapshot has the composed properties
/// already flattened into it. So every write here cuts a revision on every block type composing the
/// group — the edit is not recorded where it was made, it is recorded everywhere it lands.
/// <para>
/// The consequence worth stating plainly: editing a group used by twelve block types writes twelve
/// revisions in one transaction. That is the cost of the guarantee that published content never
/// changes underneath itself (spec section 8.5), and it is why the API reports which block types
/// were touched.
/// </para>
/// </remarks>
public interface ICompositionService
{
    /// <summary>Lists every composition, with how far each one reaches.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The compositions, or a forbidden result.</returns>
    Task<StructureResult<IReadOnlyList<CompositionSummary>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads one composition with its properties and where it is used.</summary>
    /// <param name="id">Identity of the composition.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The composition, or a not-found result.</returns>
    Task<StructureResult<CompositionDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a composition.</summary>
    /// <param name="request">The composition to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The created composition, an invalid result, or a conflict when the key is taken.</returns>
    Task<StructureResult<CompositionDetail>> CreateAsync(
        CreateCompositionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a composition's editor-facing metadata.</summary>
    /// <param name="id">Identity of the composition.</param>
    /// <param name="request">The new values. A changed key is refused.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated composition, a not-found result, or an invalid result.</returns>
    /// <remarks>
    /// Cuts nothing anywhere: a group's name never reaches a block instance, so no block type's
    /// captured schema changes.
    /// </remarks>
    Task<StructureResult<CompositionDetail>> UpdateAsync(
        int id,
        UpdateCompositionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a composition, blocked while any block type composes it.
    /// </summary>
    /// <param name="id">Identity of the composition.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>Nothing on success, a not-found result, or a conflict naming the block types.</returns>
    /// <remarks>
    /// The one delete this phase can honestly ship. The guard it needs is the composition-to-block-
    /// type join, which exists — unlike a template delete, which must ask a page table that does
    /// not. Deleting a composed group would take properties out of block types whose content is
    /// using them, so the refusal names every block type in the way.
    /// </remarks>
    Task<StructureResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Adds a property to the group and recuts every block type composing it.</summary>
    /// <param name="compositionId">Identity of the composition.</param>
    /// <param name="request">The property to add.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The stored property and the block types it reached, an invalid result when the key collides
    /// with one of those block types' own properties, or a conflict.
    /// </returns>
    Task<StructureResult<CompositionPropertySaveResult>> CreatePropertyAsync(
        int compositionId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a property and recuts every block type composing the group.</summary>
    /// <param name="compositionId">Identity of the composition.</param>
    /// <param name="propertyId">Identity of the property.</param>
    /// <param name="request">The new values. A changed key or field type is refused.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The stored property and the block types it reached, or a not-found result.</returns>
    Task<StructureResult<CompositionPropertySaveResult>> UpdatePropertyAsync(
        int compositionId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a property and recuts every block type composing the group.</summary>
    /// <param name="compositionId">Identity of the composition.</param>
    /// <param name="propertyId">Identity of the property.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What was removed and the block types it reached, or a not-found result.</returns>
    Task<StructureResult<CompositionPropertyRemovalResult>> DeletePropertyAsync(
        int compositionId,
        int propertyId,
        CancellationToken cancellationToken = default);
}
