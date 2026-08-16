using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// An <see cref="IStructureClient"/> whose every member refuses until a test overrides it.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="StubPageClient"/>, for the same reason: a component under test
/// uses one or two of thirteen members, and a stub that refuses by default makes a test's overrides
/// the whole statement of what the component talks to.
/// </remarks>
public abstract class StubStructureClient : IStructureClient
{
    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<TemplateDetail?> GetTemplateAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<TemplateDetail>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ZoneSaveResult>> CreateZoneAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ZoneSaveResult>> UpdateZoneAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ZoneRemovalResult>> DeleteZoneAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<BlockTypeDetail?> GetBlockTypeAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<BlockTypeDetail>> CreateBlockTypeAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<FieldTypeDescriptor>> GetFieldTypesAsync(
        CancellationToken cancellationToken = default) => throw Unexpected();

    private static NotSupportedException Unexpected() =>
        new("The component under test called a structure client member the test did not stub.");
}
