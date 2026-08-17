using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Preview;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Preview;
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
/// <param name="previews">Shareable preview links.</param>
/// <param name="duplication">Shallow and deep copies, which the tree's paste is built on.</param>
/// <param name="recycleBin">Soft delete, which the tree's delete is built on.</param>
/// <param name="bulk">One operation over many pages, which "publish branch" is built on.</param>
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
    ITemplateService templates,
    IPreviewTokenService previews,
    IDuplicationService duplication,
    IRecycleBinService recycleBin,
    IBulkOperationService bulk,
    ILinkResolver links) : IPageClient
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
    /// <remarks>
    /// Unpublished targets included, for the reason the endpoint gives: the caller is the backoffice,
    /// and an editor linking to a section that goes live next week has to be able to find its URL.
    /// </remarks>
    public async Task<PageLink?> ResolveLinkAsync(int id, CancellationToken cancellationToken = default)
    {
        var resolved = await links.ResolveAsync([id], includeUnpublished: true, cancellationToken);

        return resolved.TryGetValue(id, out var link)
            ? new PageLink(link.PageId, link.Url, link.IsPublished, link.Title)
            : null;
    }

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
    public async Task<StructureClientResult<PageMoveResult>> MoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await pages.MoveAsync(id, request, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PageDetail>> DuplicateAsync(
        int id,
        bool deep = false,
        int? parentId = null,
        CancellationToken cancellationToken = default) =>
        Project(await duplication.DuplicateAsync(id, deep, parentId, cancellationToken));

    /// <inheritdoc />
    public async Task<SubtreeImpact?> DescribeDeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await recycleBin.DescribeAsync(id, cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<SubtreeResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await recycleBin.DeleteAsync(id, cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecycleBinEntry>> GetRecycleBinAsync(
        CancellationToken cancellationToken = default) =>
        (await recycleBin.ListAsync(cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<SubtreeResult>> RestoreAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await recycleBin.RestoreAsync(id, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PurgeResult>> PurgeAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        var purged = await recycleBin.PurgeAsync(id, cancellationToken);

        // The service answers with a row count and the API dresses it as a PurgeResult, so this does
        // the same rather than letting the pre-rendered screen see a different shape from the
        // hydrated one.
        return purged.IsSuccess
            ? StructureClientResult<PurgeResult>.Success(new PurgeResult(id, purged.Value))
            : StructureClientResult<PurgeResult>.Failure(
                ApiDiagnostics.Project(purged.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(purged.Diagnostics, ValidationSeverity.Warning));
    }

    /// <inheritdoc />
    public async Task<StructureClientResult<BulkImpact>> PreviewBulkAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await bulk.DescribeAsync(request, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<BulkJobStatus>> StartBulkAsync(
        BulkOperationRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await bulk.StartAsync(request, cancellationToken));

    /// <inheritdoc />
    public Task<BulkJobStatus?> GetBulkAsync(
        Guid jobId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(bulk.Get(jobId).Value);

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
    public async Task<ContentDiff?> DiffDraftAsync(
        int id,
        string? contentJson,
        CancellationToken cancellationToken = default) =>
        (await diffs.CompareDraftAsync(id, contentJson, cancellationToken)).Value;

    /// <inheritdoc />
    public async Task<StructureClientResult<DraftState>> RestoreVersionAsync(
        int id,
        int versionId,
        CancellationToken cancellationToken = default) =>
        Project(await versions.RestoreAsync(id, versionId, cancellationToken));

    /// <inheritdoc />
    public async Task<IReadOnlyList<PreviewTokenSummary>> GetPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        (await previews.ListAsync(id, cancellationToken)).Value ?? [];

    /// <inheritdoc />
    public async Task<StructureClientResult<IssuedPreviewToken>> IssuePreviewTokenAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default) =>
        Project(await previews.IssueAsync(request, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<PreviewTokenSummary>> RevokePreviewTokenAsync(
        int tokenId,
        CancellationToken cancellationToken = default) =>
        Project(await previews.RevokeAsync(tokenId, cancellationToken));

    /// <inheritdoc />
    public async Task<StructureClientResult<int>> RevokeAllPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Project(await previews.RevokeAllAsync(id, cancellationToken));

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
            // A conflict is the one refusal that carries state: the draft that won the race, which
            // the losing editor needs to be offered keep-mine, take-theirs, or a diff (task P6-19).
            // Dropping it here would make the pre-rendered screen behave differently from the
            // hydrated one on the single case where the difference is a lost edit.
            : StructureClientResult<T>.Refused(
                result.Outcome is CmsOutcome.Conflict ? result.Value : default,
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Error),
                ApiDiagnostics.Project(result.Diagnostics, ValidationSeverity.Warning));
}
