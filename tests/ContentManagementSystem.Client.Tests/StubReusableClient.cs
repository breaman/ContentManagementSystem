using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// An <see cref="IReusableClient"/> whose every member refuses until a test overrides it.
/// </summary>
/// <remarks>
/// The same arrangement as <see cref="StubPageClient"/>, for the same reason.
/// </remarks>
public abstract class StubReusableClient : IReusableClient
{
    /// <inheritdoc />
    public virtual Task<IReadOnlyList<ReusableContentSummary>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<ReusableContentDetail?> GetAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
        int blockTypeId,
        int revision,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<ReusableVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<ReusableDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<ReferenceImpact> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    private static NotSupportedException Unexpected() =>
        new("The component under test called a reusable client member the test did not stub.");
}
