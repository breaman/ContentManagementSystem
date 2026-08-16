using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Client.Tests;

/// <summary>
/// An <see cref="IPageClient"/> whose every member refuses until a test overrides it.
/// </summary>
/// <remarks>
/// The interface has eighteen members and a component under test uses two or three of them. A stub
/// that refuses by default is what makes a test's overrides the whole statement of what the
/// component talks to — and makes a component that quietly started calling something else fail
/// loudly rather than receive an empty list and render an empty screen.
/// </remarks>
public abstract class StubPageClient : IPageClient
{
    /// <inheritdoc />
    public virtual Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
        int? parentId = null,
        int depth = 1,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<CursorPage<PageSummary>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<PageDetail?> GetAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
        int templateId,
        int revision,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<DraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PageMoveResult>> MoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<UnpublishResult>> UnpublishAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<PageVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<ContentDiff?> GetDiffAsync(
        int id,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<DraftState>> RestoreVersionAsync(
        int id,
        int versionId,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<PreviewTokenSummary>> GetPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<IssuedPreviewToken>> IssuePreviewTokenAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<PreviewTokenSummary>> RevokePreviewTokenAsync(
        int tokenId,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <inheritdoc />
    public virtual Task<StructureClientResult<int>> RevokeAllPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) => throw Unexpected();

    /// <summary>Builds a page summary with the members a test cares about.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="title">Editor-facing title.</param>
    /// <param name="parentId">Parent node, or null for a root page.</param>
    /// <param name="hasChildren">Whether the tree should draw an expander.</param>
    /// <param name="published">Published version number, or null for a page that is not live.</param>
    /// <param name="unpublishedChanges">Whether the draft has moved on from what is published.</param>
    /// <param name="status">Lifecycle status of the draft version.</param>
    /// <param name="scheduled">When the draft is due to go live.</param>
    /// <param name="lockedBy">Who has the page open, or null.</param>
    public static PageSummary Page(
        int id,
        string title,
        int? parentId = null,
        bool hasChildren = false,
        int? published = 1,
        bool unpublishedChanges = false,
        string status = "Draft",
        DateTimeOffset? scheduled = null,
        string? lockedBy = null) =>
        new(
            id,
            Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
            parentId,
            title,
            title.ToLowerInvariant().Replace(' ', '-'),
            Depth: parentId is null ? 0 : 1,
            SortOrder: id,
            TemplateId: 1,
            TemplateKey: "marketing-landing",
            Status: status,
            HasUnpublishedChanges: unpublishedChanges,
            DraftVersionNumber: 2,
            PublishedVersionNumber: published,
            ShowInNavigation: true,
            HasChildren: hasChildren,
            ModifiedOn: DateTimeOffset.UnixEpoch,
            ScheduledPublishOn: scheduled,
            LockedBy: lockedBy);

    private static NotSupportedException Unexpected() =>
        new("The component under test called a page client member the test did not arrange.");
}
