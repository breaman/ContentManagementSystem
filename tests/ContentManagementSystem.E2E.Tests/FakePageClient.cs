using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Feeds the page screens a fixed site so the accessibility gate has markup to check (task P2-23).
/// </summary>
/// <remarks>
/// Fixed and deliberately varied, for the reason <see cref="FakeStructureClient"/> gives: axe only
/// has an opinion about table headers, form labels, and reading order once there are rows and
/// controls on the page. The fixture therefore includes a nested page, a published one, a draft with
/// unpublished changes, a required zone, a zone of a field type the plain editor cannot edit, and a
/// diff carrying every change kind — which between them put every badge, control, and branch of
/// these screens into the markup axe inspects.
/// </remarks>
public sealed class FakePageClient : IPageClient
{
    /// <summary>Identity of the page the editor and history screens are rendered against.</summary>
    public const int Id = 1;

    /// <summary>Identity of the published version, which the diff compares from.</summary>
    public const int PublishedVersionId = 21;

    /// <summary>Identity of the draft version, which the diff compares to.</summary>
    public const int DraftVersionId = 22;

    private static readonly Guid MovedBlock = new("0f6c0d1e-2a3b-4c5d-8e9f-101112131415");

    /// <inheritdoc />
    public Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
        int? parentId = null,
        int depth = 1,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageTreeNode>>(
        [
            new PageTreeNode(
                Summary(Id, "Pricing", "pricing", depth: 0, published: 4, unpublished: true, children: true),
                [
                    new PageTreeNode(
                        Summary(2, "Enterprise", "enterprise", depth: 1, published: 2, unpublished: false),
                        []),
                ]),
            // Never published, so the third of the list's three state badges is on the page too.
            new PageTreeNode(Summary(3, "Careers", "careers", depth: 0, published: null, unpublished: false), []),
        ]);

    /// <inheritdoc />
    public Task<CursorPage<PageSummary>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new CursorPage<PageSummary>(
            [Summary(Id, "Pricing", "pricing", depth: 0, published: 4, unpublished: true, children: true)],
            null));

    /// <inheritdoc />
    public Task<PageDetail?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<PageDetail?>(new PageDetail(
            Summary(Id, "Pricing", "pricing", depth: 0, published: 4, unpublished: true, children: true),
            """
            {
              "schemaVersion": 1,
              "templateKey": "marketing-landing",
              "templateRevision": 3,
              "zones": {
                "heroTitle": { "type": "plainText", "value": "What our plans cost" },
                "body": { "type": "richText", "format": "markdown", "value": "Three tiers." },
                "photo": { "type": "media", "mediaId": 7 }
              }
            }
            """,
            TemplateRevision: 3,
            UseExplicitUrl: false,
            ExplicitUrl: null,
            OwnerUserId: 1,
            ReviewByDate: null,
            InternalNotes: null,
            new PageSeo(null, "What our plans cost.", null, true, true, null, null, null, null, null,
                null, null, null),
            RowVersion: "AAAAAAAAB9E="));

    /// <inheritdoc />
    public Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
        int templateId,
        int revision,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CapturedSlot>>(
        [
            new CapturedSlot("heroTitle", "Hero title", "plainText", IsRequired: true, 0, null),
            new CapturedSlot("body", "Body", "richText", IsRequired: false, 1, null),
            // A field type the plain editor shows read-only, so that branch is rendered too.
            new CapturedSlot("photo", "Photo", "media", IsRequired: false, 2, null),
        ]);

    /// <inheritdoc />
    public Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TemplateSummary>>(
        [
            new TemplateSummary(1, "marketing-landing", "Marketing landing page", "Hero and body.", null,
                IsOrphaned: false, IsEnabled: true, CurrentRevision: 3, SortOrder: 0, ZoneCount: 3),
            new TemplateSummary(2, "retired", "Retired shape", null, null,
                IsOrphaned: false, IsEnabled: false, CurrentRevision: 9, SortOrder: 1, ZoneCount: 1),
        ]);

    /// <inheritdoc />
    public async Task<StructureClientResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default) =>
        StructureClientResult<PageDetail>.Success((await GetAsync(Id, cancellationToken))!);

    /// <inheritdoc />
    public Task<StructureClientResult<DraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<DraftSaveResult>.Success(
            new DraftSaveResult(
                new DraftState(Id, DraftVersionId, 5, request.ContentJson ?? "{}", "marketing-landing", 3,
                    "AAAAAAAAB9I=", DateTimeOffset.UtcNow),
                [],
                1)));

    /// <inheritdoc />
    public async Task<StructureClientResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default) =>
        StructureClientResult<PageDetail>.Success((await GetAsync(id, cancellationToken))!);

    /// <inheritdoc />
    public Task<StructureClientResult<PublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<PublishValidation>.Success(
            new PublishValidation(
                false,
                [new ApiDiagnostic("field.required", "\"Hero title\" is required.", "zones.heroTitle")],
                [new ApiDiagnostic("link.target-unpublished",
                    "\"Get started\" links to \"Enterprise\", which is not published.")])));

    /// <inheritdoc />
    public Task<StructureClientResult<PublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<PublishResult>.Success(
            new PublishResult(id, DraftVersionId, 5, DateTimeOffset.UtcNow, 4, 1, [])));

    /// <inheritdoc />
    public Task<StructureClientResult<UnpublishResult>> UnpublishAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<UnpublishResult>.Success(new UnpublishResult(id, 4)));

    /// <inheritdoc />
    public Task<IReadOnlyList<PageVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PageVersionSummary>>(
        [
            new PageVersionSummary(DraftVersionId, 5, "Draft", null, "Pricing", 3,
                IsDraft: true, IsPublished: false, DateTimeOffset.UtcNow, 1, null, null),
            new PageVersionSummary(PublishedVersionId, 4, "Published", null, "Pricing", 3,
                IsDraft: false, IsPublished: true, DateTimeOffset.UtcNow.AddDays(-1), 1,
                DateTimeOffset.UtcNow.AddDays(-1), 1),
            // A named checkpoint, so the history's third status badge is rendered.
            new PageVersionSummary(20, 3, "Archived", "before the big rewrite", "Pricing (old)", 2,
                IsDraft: false, IsPublished: false, DateTimeOffset.UtcNow.AddDays(-9), 1, null, null),
        ]);

    /// <inheritdoc />
    public Task<ContentDiff?> GetDiffAsync(
        int id,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ContentDiff?>(new ContentDiff(
            id,
            fromVersionId,
            toVersionId,
            4,
            5,
            [new MetadataChange("Title", "Plans", "Pricing")],
            [
                new ZoneChange("heroTitle", "plainText", ContentChangeKind.Changed, "What plans cost",
                    "What our plans cost",
                    [
                        new TextSegment("What ", ContentChangeKind.Unchanged),
                        new TextSegment("our ", ContentChangeKind.Added),
                        new TextSegment("plans cost", ContentChangeKind.Unchanged),
                    ],
                    []),
                new ZoneChange("sections", "blocks", ContentChangeKind.Changed, null, null, [],
                    [
                        // The change kind this whole diff exists to report (criterion P2 #6).
                        new BlockChange(MovedBlock, "rawHtml", ContentChangeKind.Moved, 0, 1, []),
                        new BlockChange(Guid.NewGuid(), "callout", ContentChangeKind.Added, null, 0,
                            [
                                new PropertyChange("heading", "plainText", ContentChangeKind.Added,
                                    null, "New for 2026", []),
                            ]),
                    ]),
            ]));

    /// <inheritdoc />
    public Task<StructureClientResult<DraftState>> RestoreVersionAsync(
        int id,
        int versionId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<DraftState>.Success(
            new DraftState(id, DraftVersionId, 5, "{}", "marketing-landing", 3, "AAAAAAAAB9M=",
                DateTimeOffset.UtcNow)));

    /// <inheritdoc />
    /// <remarks>
    /// Three links, in the three states the screen renders differently — live, revoked, and expired.
    /// A fixture with only live rows would leave two of the three badge branches out of the markup
    /// axe inspects, which is the whole reason this class is varied rather than minimal.
    /// </remarks>
    public Task<IReadOnlyList<PreviewTokenSummary>> GetPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PreviewTokenSummary>>(
        [
            new PreviewTokenSummary(
                31, id, DraftVersionId, 5, "Draft", DateTimeOffset.UtcNow.AddDays(-1), 1,
                DateTimeOffset.UtcNow.AddDays(6), null, 2, null, true, "Sent to the agency"),
            new PreviewTokenSummary(
                32, id, DraftVersionId, 5, "Draft", DateTimeOffset.UtcNow.AddDays(-4), 1,
                DateTimeOffset.UtcNow.AddDays(3), 1, 1, DateTimeOffset.UtcNow.AddDays(-2), false,
                "Revoked after the review"),
            new PreviewTokenSummary(
                33, id, PublishedVersionId, 4, "Published", DateTimeOffset.UtcNow.AddDays(-40), 1,
                DateTimeOffset.UtcNow.AddDays(-10), null, 7, null, false, null),
        ]);

    /// <inheritdoc />
    public Task<StructureClientResult<IssuedPreviewToken>> IssuePreviewTokenAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.FromResult(StructureClientResult<IssuedPreviewToken>.Success(
            new IssuedPreviewToken(
                new PreviewTokenSummary(
                    34, request.PageId, DraftVersionId, 5, "Draft", DateTimeOffset.UtcNow, 1,
                    DateTimeOffset.UtcNow.AddDays(7), request.MaxUses, 0, null, true, request.Notes),
                "not-a-real-token",
                "/preview/s/not-a-real-token")));
    }

    /// <inheritdoc />
    public Task<StructureClientResult<PreviewTokenSummary>> RevokePreviewTokenAsync(
        int tokenId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<PreviewTokenSummary>.Success(
            new PreviewTokenSummary(
                tokenId, Id, DraftVersionId, 5, "Draft", DateTimeOffset.UtcNow.AddDays(-1), 1,
                DateTimeOffset.UtcNow.AddDays(6), null, 2, DateTimeOffset.UtcNow, false, null)));

    /// <inheritdoc />
    public Task<StructureClientResult<int>> RevokeAllPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(StructureClientResult<int>.Success(1));

    private static PageSummary Summary(
        int id,
        string title,
        string slug,
        int depth,
        int? published,
        bool unpublished,
        bool children = false) =>
        new(
            id,
            Guid.Parse($"00000000-0000-0000-0000-{id:D12}"),
            depth == 0 ? null : Id,
            title,
            slug,
            depth,
            0,
            TemplateId: 1,
            TemplateKey: "marketing-landing",
            Status: "Draft",
            HasUnpublishedChanges: unpublished,
            DraftVersionNumber: 5,
            PublishedVersionNumber: published,
            ShowInNavigation: true,
            HasChildren: children,
            ModifiedOn: DateTimeOffset.UtcNow);
}
