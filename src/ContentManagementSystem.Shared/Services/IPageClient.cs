using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// What the page admin screens need from the server (task P2-23).
/// </summary>
/// <remarks>
/// Implemented twice, exactly as <see cref="IStructureClient"/> is: over <c>HttpClient</c> in the
/// WebAssembly backoffice, and directly over the page services on the server so a screen
/// pre-renders with real content instead of a spinner the editor watches.
/// <para>
/// Reads return bare values and writes return <see cref="StructureClientResult{T}"/>, for the reason
/// the structure client gives — a failed read is an empty state or a transport fault, while a failed
/// write is a rule the person needs read back to them, which is why the API returns diagnostics at
/// all. Publishing is the case that makes this matter most: an unfilled required zone comes back as
/// a list of zones to go and fill in, not as a red banner saying "422".
/// </para>
/// </remarks>
public interface IPageClient
{
    /// <summary>Fetches a slice of the content tree.</summary>
    /// <param name="parentId">Node to expand, or null for the root of the site.</param>
    /// <param name="depth">How many levels below the node to return.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<PageTreeNode>> GetTreeAsync(
        int? parentId = null,
        int depth = 1,
        CancellationToken cancellationToken = default);

    /// <summary>Lists pages matching a set of filters.</summary>
    /// <param name="query">Filters and paging.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<CursorPage<PageSummary>> ListAsync(
        PageQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a page's metadata and its draft payload.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<PageDetail?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Resolves a page's current URL, for writing a link into rich text (task P6-11).</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>Where the page sits, or null when there is no such page.</returns>
    /// <remarks>
    /// Only prose needs this. A <c>link</c> or <c>pageReference</c> property stores the id and lets
    /// the renderer resolve it late, which is what makes those links survive a move (ADR-0006); a
    /// markdown or HTML zone is text and has to carry an address.
    /// </remarks>
    Task<PageLink?> ResolveLinkAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Reads the zone definitions of the revision a draft was authored against.</summary>
    /// <param name="templateId">Identity of the template.</param>
    /// <param name="revision">The captured revision (spec section 8.5).</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <remarks>
    /// The captured revision, never the template's current one. A page authored before a zone was
    /// added has no value under that key and must not be shown a control for it as though it did —
    /// adopting a structural change is a deliberate act, not something a screen does by rendering.
    /// </remarks>
    Task<IReadOnlyList<CapturedSlot>> GetZonesAsync(
        int templateId,
        int revision,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the templates a new page can be created from.</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates a page from a template.</summary>
    /// <param name="request">The page to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Saves the draft payload.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="request">The payload and the row version the editor last saw.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<DraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Applies a partial update to a page's title, slug, SEO, and editorial metadata.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="request">The members to change.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reparents or reorders a page, rebuilding the URLs beneath it (task P6-03).
    /// </summary>
    /// <param name="id">Identity of the page to move.</param>
    /// <param name="request">The new parent and position, and whether this is a preview.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What the move did, or — for a preview — what it would do.</returns>
    /// <remarks>
    /// Called twice by the tree: once with <c>Preview: true</c> to fill the confirmation dialog, and
    /// again without it if the editor goes ahead. Both calls run the same code on the server, which
    /// is what stops the dialog promising one thing and the move doing another.
    /// </remarks>
    Task<StructureClientResult<PageMoveResult>> MoveAsync(
        int id,
        MovePageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies a page, and optionally everything beneath it (task P6-04, spec section 14.12).
    /// </summary>
    /// <param name="id">Identity of the page to copy.</param>
    /// <param name="deep">Whether to copy the subtree as well as the page.</param>
    /// <param name="parentId">
    /// Where the copy lands, or null to put it beside the original. Naming a parent is what turns
    /// duplication into the paste half of the tree's copy-and-paste.
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PageDetail>> DuplicateAsync(
        int id,
        bool deep = false,
        int? parentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports what deleting a page would take with it, without deleting anything (task P6-04).
    /// </summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The subtree's size and how much of it is live.</returns>
    Task<SubtreeImpact?> DescribeDeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a page and its subtree into the recycle bin (task P6-04, spec section 14.10).
    /// </summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>Every page that went with it, and how many of them were live.</returns>
    Task<StructureClientResult<SubtreeResult>> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the publish checks without publishing.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<StructureClientResult<PublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the current draft.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="acknowledgeWarnings">Whether the editor has seen the warnings and still wants to.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>Retires a page from the public site.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<UnpublishResult>> UnpublishAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a page's version history, newest first.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<IReadOnlyList<PageVersionSummary>> GetVersionsAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Compares two of a page's versions.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="fromVersionId">The earlier version.</param>
    /// <param name="toVersionId">The later version.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    Task<ContentDiff?> GetDiffAsync(
        int id,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default);

    /// <summary>Copies a version into the draft.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="versionId">Identity of the version to restore.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<DraftState>> RestoreVersionAsync(
        int id,
        int versionId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the shareable preview links issued for a page, newest first.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <remarks>
    /// Revoked and expired links are in the list. The screen's most common question is not "which
    /// links work" but "why did the one I sent stop working", and only a list that keeps them can
    /// answer it.
    /// </remarks>
    Task<IReadOnlyList<PreviewTokenSummary>> GetPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a shareable preview link.
    /// </summary>
    /// <param name="request">Which version, for how long, and how many views.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The link, including the secret — which is returned once and never again.</returns>
    Task<StructureClientResult<IssuedPreviewToken>> IssuePreviewTokenAsync(
        CreatePreviewTokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes one preview link.</summary>
    /// <param name="tokenId">Identity of the token row.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<StructureClientResult<PreviewTokenSummary>> RevokePreviewTokenAsync(
        int tokenId,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes every live preview link for a page.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>How many links were revoked.</returns>
    Task<StructureClientResult<int>> RevokeAllPreviewTokensAsync(
        int id,
        CancellationToken cancellationToken = default);
}
