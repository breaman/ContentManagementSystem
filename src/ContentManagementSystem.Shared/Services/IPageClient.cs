using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
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
}
