using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Feeds the reusable-content screens a fixed library so the accessibility gate has markup to check
/// (task P4-11).
/// </summary>
/// <remarks>
/// Fixed and deliberately varied, for the reason <see cref="FakeStructureClient"/> gives: axe only
/// has an opinion about table headers, form labels, and reading order once there are rows and
/// controls on the page. The fixture therefore carries a live item, one that has never been
/// published, one with unpublished changes, an impact list with a pinned page and a draft-only page
/// in it, and a version history — which between them put every badge and branch of these screens
/// into the markup.
/// <para>
/// It also backs <c>PinnedPlacements</c> on the page editor, which is why
/// <see cref="StalePinnedVersionId"/> and the published version in <see cref="GetVersionsAsync"/>
/// deliberately differ: a pin that matched would render the panel with no stale badge and leave the
/// branch that matters unchecked.
/// </para>
/// </remarks>
public sealed class FakeReusableClient : IReusableClient
{
    /// <summary>Identity of the item the screens are rendered against.</summary>
    public const int Id = 3;

    /// <summary>The version <see cref="FakePageClient"/>'s payload pins, which is not the live one.</summary>
    public const int StalePinnedVersionId = 41;

    /// <summary>Identity of the version currently published.</summary>
    public const int PublishedVersionId = 42;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReusableContentSummary>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReusableContentSummary>>(
        [
            Summary(Id, "Site footer", "site-footer", draft: 3, published: 2, unpublished: true),
            Summary(4, "Spring banner", "spring-banner", draft: 1, published: null, unpublished: false),
            Summary(5, "Legal notice", "legal-notice", draft: 2, published: 2, unpublished: false),
        ]);

    /// <inheritdoc />
    public Task<ReusableContentDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<ReusableContentDetail?>(new ReusableContentDetail(
            Summary(Id, "Site footer", "site-footer", draft: 3, published: 2, unpublished: true),
            """
            {
              "schemaVersion": 1,
              "templateKey": "rawHtml",
              "templateRevision": 1,
              "zones": {
                "content": { "type": "html", "value": "<p>© Contoso</p>" }
              }
            }
            """,
            BlockTypeRevision: 1,
            RowVersion: "AAAAAAAAB9M="));

    /// <inheritdoc />
    public Task<IReadOnlyList<CapturedSlot>> GetPropertiesAsync(
        int blockTypeId,
        int revision,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CapturedSlot>>(
        [
            new CapturedSlot("content", "HTML", "html", IsRequired: true, 0, null),
            // A field type the plain editor shows read-only, so that branch is rendered too.
            new CapturedSlot("badge", "Badge image", "media", IsRequired: false, 1, null),
        ]);

    /// <inheritdoc />
    public Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockTypeSummary>>(
        [
            new BlockTypeSummary(1, "rawHtml", "Raw HTML", "A single block of HTML.", null, "code",
                SummaryTemplate: "{content}", IsOrphaned: false, IsBuiltIn: true, CurrentRevision: 1,
                PropertyCount: 1),
        ]);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default) =>
        GetAsync(Id, cancellationToken)
            .ContinueWith(
                task => StructureClientResult<ReusableContentDetail>.Success(task.Result!),
                cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Current);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default) =>
        CreateAsync(new CreateReusableContentRequest(1, "Site footer"), cancellationToken);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<ReusableDraftSaveResult>.Success(
            new ReusableDraftSaveResult(
                new ReusableDraftState(Id, 43, 3, request?.ContentJson ?? "{}", "rawHtml", 1,
                    "AAAAAAAAB9Q=", DateTimeOffset.UtcNow),
                [],
                0)));

    /// <inheritdoc />
    public Task<IReadOnlyList<ReusableVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ReusableVersionSummary>>(
        [
            new ReusableVersionSummary(43, 3, "Draft", null, 1, IsDraft: true, IsPublished: false,
                DateTimeOffset.UtcNow, 1, null, null),
            new ReusableVersionSummary(PublishedVersionId, 2, "Published", null, 1, IsDraft: false,
                IsPublished: true, DateTimeOffset.UtcNow, 1, DateTimeOffset.UtcNow, 1),
            new ReusableVersionSummary(StalePinnedVersionId, 1, "Archived", "before the rebrand", 1,
                IsDraft: false, IsPublished: false, DateTimeOffset.UtcNow, 1, DateTimeOffset.UtcNow, 1),
        ]);

    /// <inheritdoc />
    public Task<StructureClientResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<ReusablePublishValidation>.Success(
            new ReusablePublishValidation(
                true,
                [],
                [new ApiDiagnostic(ReusableCodes.BlastRadius, "3 published pages will change immediately.")],
                Impact)));

    /// <inheritdoc />
    public Task<StructureClientResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<ReusablePublishResult>.Success(
            new ReusablePublishResult(Id, 44, 4, DateTimeOffset.UtcNow, 2, 1, Impact, [])));

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<ReusableUnpublishResult>.Success(
            new ReusableUnpublishResult(Id, 2, Impact)));

    /// <inheritdoc />
    public Task<StructureClientResult<ReusableDeleteResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<ReusableDeleteResult>.Failure(
            ReusableCodes.StillReferenced,
            "Stored content on 3 pages still places this item. Replace or remove those placements first."));

    /// <inheritdoc />
    public Task<ReferenceImpact> WhereUsedAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Impact);

    /// <summary>
    /// An impact carrying one of each row state the where-used panel can draw.
    /// </summary>
    /// <remarks>
    /// Shared with <see cref="FakeMediaClient"/>, which renders the same panel beside the media
    /// item's delete buttons. One fixture, because a second copy would be the one that stopped
    /// carrying a pinned row and left that branch unchecked on whichever screen owned it.
    /// </remarks>
    public static ReferenceImpact Impact { get; } =
        new(
            [
                new AffectedPage(1, "Pricing", "/pricing", IsPublished: true, IsPinned: false, "footer"),
                new AffectedPage(2, "Enterprise", "/pricing/enterprise", IsPublished: true,
                    IsPinned: true, "footer"),
                // Reached through another item, so no zone was recorded on the page itself.
                new AffectedPage(3, "Careers", null, IsPublished: true, IsPinned: false),
                new AffectedPage(6, "Draft page", null, IsPublished: false, IsPinned: false, "footer"),
            ],
            AffectedPageCount: 3,
            PinnedPageCount: 1,
            [new AffectedReusableItem(5, "legal-notice", "Legal notice", IsPublished: true)],
            [new ApiDiagnostic(ReusableCodes.BlastRadius, "3 published pages will change immediately.")],
            IsTruncated: false);

    private static ReusableContentSummary Summary(
        int id,
        string name,
        string key,
        int draft,
        int? published,
        bool unpublished) =>
        new(id, key, name, $"{name}, shown on several pages.", null, 1, "rawHtml",
            "Draft", unpublished, draft, published, DateTimeOffset.UtcNow);
}
