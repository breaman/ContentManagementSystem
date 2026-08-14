using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Reads and writes block types, their properties, and the compositions composed into them
/// (task P1-23).
/// </summary>
/// <remarks>
/// One service rather than three, because all of these changes cut the same artefact: a
/// <c>BlockTypeRevision</c> whose snapshot is the flattened property set. Splitting them would put
/// the flattening rule in three places and leave the revision number free to be computed twice
/// against one block type.
/// <para>
/// Every method authorizes the caller itself, as <see cref="ITemplateService"/> does. The evolution
/// rules of spec section 8.5 apply to properties exactly as they do to zones — add free, remove
/// retains the payload data, key rename forbidden, field-type change forbidden — because a block
/// property and a zone are the same thing at validation time.
/// </para>
/// <para>
/// There is deliberately no block type delete, for the reason there is no template delete: it must
/// be blocked while content references the type, and there is no page table to ask until Phase 2.
/// <c>IsBuiltIn</c> is the guard already in place for the seeded <c>rawHtml</c> type, and it refuses
/// structural edits today rather than waiting for the delete verb to exist.
/// </para>
/// </remarks>
public interface IBlockTypeService
{
    /// <summary>Lists every block type, in picker order.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The block types, or a forbidden result.</returns>
    Task<CmsResult<IReadOnlyList<BlockTypeSummary>>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Reads one block type with its own, composed, and effective property sets.</summary>
    /// <param name="id">Identity of the block type.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The block type, or a not-found result.</returns>
    Task<CmsResult<BlockTypeDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a block type and cuts its first revision.</summary>
    /// <param name="request">The block type to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The created block type, an invalid result, or a conflict when the key is taken.</returns>
    Task<CmsResult<BlockTypeDetail>> CreateAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a block type's editor-facing metadata. Cuts no revision.</summary>
    /// <param name="id">Identity of the block type.</param>
    /// <param name="request">The new values. A changed key is refused.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated block type, a not-found result, or an invalid result.</returns>
    Task<CmsResult<BlockTypeDetail>> UpdateAsync(
        int id,
        UpdateBlockTypeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a block type's structural revisions, newest first.</summary>
    /// <param name="id">Identity of the block type.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The revision history, or a not-found result.</returns>
    Task<CmsResult<IReadOnlyList<BlockTypeRevisionSummary>>> ListRevisionsAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one revision and the flattened property set it captured.</summary>
    /// <param name="id">Identity of the block type.</param>
    /// <param name="revisionNumber">The revision number, as a block instance records it.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The revision, or a not-found result.</returns>
    Task<CmsResult<BlockTypeRevisionDetail>> GetRevisionAsync(
        int id,
        int revisionNumber,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a property to a block type and cuts a new revision.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="request">The property to add.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The stored property with any warnings, an invalid result, or a conflict.</returns>
    Task<CmsResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a property, cutting a revision only if content is read differently after.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="propertyId">Identity of the property.</param>
    /// <param name="request">The new values. A changed key or field type is refused.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The stored property with any warnings, or a not-found or invalid result.</returns>
    Task<CmsResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a property definition and cuts a new revision.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="propertyId">Identity of the property.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What was removed and the resulting revision, or a not-found result.</returns>
    /// <remarks>
    /// Unguarded, like removing a zone: values already stored under the key survive in their block
    /// instances and are reported as orphaned rather than invalid (spec section 8.5).
    /// </remarks>
    Task<CmsResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default);

    /// <summary>Composes a shared property group into a block type and cuts a new revision.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="request">Which composition to compose, and where in the order.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The block type as it now stands, or an invalid result when a composed key collides with one
    /// the block type already has.
    /// </returns>
    Task<CmsResult<BlockTypeDetail>> AttachCompositionAsync(
        int blockTypeId,
        AttachCompositionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a composed group from a block type and cuts a new revision.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="compositionId">Identity of the composition.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The block type as it now stands, or a not-found result.</returns>
    Task<CmsResult<BlockTypeDetail>> DetachCompositionAsync(
        int blockTypeId,
        int compositionId,
        CancellationToken cancellationToken = default);
}
