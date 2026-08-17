using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IReusableClient"/>, over the services directly (task P4-11).
/// </summary>
/// <param name="reusable">The whole reusable-content lifecycle.</param>
/// <param name="blockTypes">Block types, and the revision snapshots the editor form is built from.</param>
/// <param name="gate">Keeps concurrently initializing components off each other's database work.</param>
/// <remarks>
/// Used during pre-rendering, so a library or editor screen arrives with its content already in the
/// HTML. It calls the services rather than looping back through the HTTP API — a request to itself
/// would need a cookie it does not have and an antiforgery token that has not been issued yet.
/// <para>
/// Authorization is unaffected by the shortcut: <c>ReusableContentService</c> checks the caller's
/// permissions itself, against the same request principal the API would have seen.
/// </para>
/// </remarks>
public sealed class ServerReusableClient(
    IReusableContentService reusable,
    IBlockTypeService blockTypes,
    PrerenderGate gate) : IReusableClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ReusableContentSummary>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => reusable.ListAsync(folderId, search, token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<ReusableContentDetail?> GetAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => reusable.GetAsync(id, token), cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
        int blockTypeId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var detail = await gate.RunAsync(
            token => blockTypes.GetRevisionAsync(blockTypeId, revision, token),
            cancellationToken);

        return detail.Value is null ? [] : CapturedSlot.Read(detail.Value.Properties);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => blockTypes.ListAsync(token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => reusable.CreateAsync(request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => reusable.PatchAsync(id, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => reusable.SaveDraftAsync(id, request, token), cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReusableVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(token => reusable.ListVersionsAsync(id, token), cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => reusable.ValidateAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => reusable.PublishAsync(id, acknowledgeWarnings, token), cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(
            token => reusable.UnpublishAsync(id, acknowledgeWarnings, token),
            cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<ReusableDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await gate.RunAsync(token => reusable.DeleteAsync(id, token), cancellationToken));

    /// <inheritdoc />
    public async Task<ReferenceImpact> WhereUsedAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await gate.RunAsync(
            token => reusable.WhereUsedAsync(id, token),
            cancellationToken)).Value ?? ReferenceImpact.None;

    /// <summary>
    /// Re-expresses a service result as the shape the screens read.
    /// </summary>
    /// <remarks>
    /// Warnings survive a success and errors survive a failure, which is the whole point: a publish
    /// refused for an unacknowledged blast radius carries the count the confirmation dialog shows,
    /// and flattening it to a boolean would leave the dialog with nothing to say.
    /// </remarks>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess && result.Value is not null
            ? StructureClientResult<T>.Success(
                result.Value,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
