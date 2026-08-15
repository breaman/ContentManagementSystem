using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IPageClient"/>, over the page services directly (task P2-23).
/// </summary>
/// <param name="pages">Page reads, creation, and metadata.</param>
/// <param name="drafts">Draft reads and writes.</param>
/// <param name="publishing">Publish, unpublish, and the dry-run check.</param>
/// <param name="versions">History and restore.</param>
/// <param name="diffs">Version comparison.</param>
/// <param name="templates">Templates, and the revision snapshots the editor form is built from.</param>
/// <remarks>
/// Used during pre-rendering, so a page screen arrives with its content already in the HTML rather
/// than showing a spinner until the WebAssembly runtime finishes downloading. It calls the services
/// rather than looping back through its own HTTP API — a request to itself would need a cookie it
/// does not have and an antiforgery token that has not been issued yet.
/// <para>
/// Authorization is unaffected by the shortcut: every one of these services checks the caller's
/// permissions itself, against the same request principal the API would have seen.
/// </para>
/// </remarks>
public sealed class ServerPageClient(
    IPageService pages,
    IDraftService drafts,
    IPublishingService publishing,
    IVersionService versions,
    IContentDiffService diffs,
    ITemplateService templates) : IPageClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
        int? parentId = null,
        int depth = 1,
        CancellationToken cancellationToken = default) =>
        (await pages.TreeAsync(parentId, depth, cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<CursorPage<PageSummary>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default) =>
        (await pages.ListAsync(query, cancellationToken)).Value ?? CursorPage<PageSummary>.Empty;

    /// <inheritdoc />
    public async Task<PageDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        (await pages.GetAsync(id, cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
        int templateId,
        int revision,
        CancellationToken cancellationToken = default)
    {
        var detail = await templates.GetRevisionAsync(templateId, revision, cancellationToken);

        return detail.Value is null ? [] : CapturedSlot.Read(detail.Value.Zones);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        (await templates.ListAsync(cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await pages.CreateAsync(request, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<DraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await drafts.SaveAsync(id, request, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await pages.PatchMetadataAsync(id, request, cancellationToken: cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await publishing.ValidateAsync(id, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        Project(await publishing.PublishAsync(id, acknowledgeWarnings, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<UnpublishResult>> UnpublishAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var result = await publishing.UnpublishAsync(id, cancellationToken);

        return result.IsSuccess
            ? StructureClientResult<UnpublishResult>.Success(new UnpublishResult(id, result.Value))
            : StructureClientResult<UnpublishResult>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PageVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await versions.ListAsync(id, cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<ContentDiff?> GetDiffAsync(
        int id,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default) =>
        (await diffs.CompareAsync(id, fromVersionId, toVersionId, cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<DraftState>> RestoreVersionAsync(
        int id,
        int versionId,
        CancellationToken cancellationToken = default) =>
        Project(await versions.RestoreAsync(id, versionId, cancellationToken));

    /// <summary>
    /// Folds a service result into the shape the screens read.
    /// </summary>
    /// <remarks>
    /// The outcome enum is deliberately dropped. It exists so the API can pick a status code, and a
    /// screen rendering on the server has no status code to pick — what it needs is the same "did it
    /// work, and what do I show" the HTTP client half produces, so that one component works either
    /// side of the pre-render boundary.
    /// </remarks>
    private static StructureClientResult<T> Project<T>(CmsResult<T> result) =>
        result.IsSuccess
            ? StructureClientResult<T>.Success(
                result.Value!,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning))
            : StructureClientResult<T>.Failure(
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
