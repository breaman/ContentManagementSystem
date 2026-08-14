using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the structure admin screens need from the server (task P1-29).
/// </summary>
/// <remarks>
/// Implemented twice, per the project's pre-rendering pattern: over <c>HttpClient</c> in the
/// WebAssembly client, and directly over the structure services on the server, so a screen renders
/// with real content during pre-render instead of a spinner the user watches until the runtime
/// finishes downloading.
/// <para>
/// The reads return bare values and the writes return <see cref="StructureClientResult{T}"/>. That
/// asymmetry is deliberate: a failed read is either "not found", which the screen shows as an empty
/// state, or a transport fault the error boundary owns — whereas a failed write is a content-model
/// rule the developer needs read back to them, which is the whole reason the API returns diagnostics
/// at all.
/// </para>
/// </remarks>
public interface IStructureClient
{
    /// <summary>Lists every template.</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one template with its zones.</summary>
    /// <param name="id">Identity of the template.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<TemplateDetail?> GetTemplateAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a template.</summary>
    /// <param name="request">The template to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<TemplateDetail>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a zone to a template.</summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="request">The zone to add.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ZoneSaveResult>> CreateZoneAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a zone.</summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="zoneId">Identity of the zone.</param>
    /// <param name="request">The new values.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ZoneSaveResult>> UpdateZoneAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a zone definition.</summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="zoneId">Identity of the zone.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<ZoneRemovalResult>> DeleteZoneAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every block type.</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one block type with its own and composed properties.</summary>
    /// <param name="id">Identity of the block type.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<BlockTypeDetail?> GetBlockTypeAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a block type.</summary>
    /// <param name="request">The block type to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<BlockTypeDetail>> CreateBlockTypeAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Adds a property to a block type.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="request">The property to add.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Updates a block type property.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="propertyId">Identity of the property.</param>
    /// <param name="request">The new values.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a block type property.</summary>
    /// <param name="blockTypeId">Identity of the block type.</param>
    /// <param name="propertyId">Identity of the property.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the registered field types, which is what a slot's field type picker offers.
    /// </summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<FieldTypeDescriptor>> GetFieldTypesAsync(CancellationToken cancellationToken = default);
}
