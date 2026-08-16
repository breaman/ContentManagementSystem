using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// The whole life of a reusable content item: create, edit its draft, publish it, retire it, delete
/// it (task P4-03, spec section 9).
/// </summary>
/// <remarks>
/// One service where pages have three — <c>PageService</c>, <c>DraftService</c>,
/// <c>PublishingService</c> — because the thing being managed is far smaller. A reusable item has no
/// tree to walk, no URL to materialize, no redirects to emit, no SEO panel and no template: strip
/// those from the page services and what is left of each is a handful of methods that all read the
/// same two rows. Splitting it three ways would be three files of loading code around one entity.
/// <para>
/// <strong>What it does not reimplement is the point.</strong> Version numbering is
/// <c>VersionNumbers</c>, payload checking is <c>IContentSchemaValidator</c>, reference projection is
/// <c>IContentReferenceProjector</c>, and impact is <c>IReferenceQueryService</c> — the same
/// primitives publishing a page uses, called with <c>ReusableContentVersion</c> as the source type.
/// A second implementation of any of them is a second place for the guarantees of spec section 11.2
/// to stop holding.
/// </para>
/// <para>
/// The mechanism behind goal G4 is worth stating outright, because it is an absence: nothing here
/// touches a page. Publishing an item repoints <c>ReusableContent.PublishedVersionId</c> and stops,
/// and every late-bound placement renders whatever that pointer names at the moment the page is
/// served. That is what makes "one publish updates forty pages without republishing them" a property
/// of the data rather than a fan-out somebody has to get right (acceptance criterion P4 #2).
/// </para>
/// </remarks>
public interface IReusableContentService
{
    /// <summary>Lists the items in the library.</summary>
    /// <param name="folderId">Restrict to one folder, or null for every folder.</param>
    /// <param name="search">Case-insensitive fragment matched against key and name, or null.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The live items, ordered by name.</returns>
    Task<CmsResult<IReadOnlyList<ReusableContentSummary>>> ListAsync(
        int? folderId = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one item and its draft payload.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The item, or a not-found result.</returns>
    Task<CmsResult<ReusableContentDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates an item and its first, empty draft.</summary>
    /// <param name="request">Shape, name, and key.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The created item, or every rule the request broke.</returns>
    Task<CmsResult<ReusableContentDetail>> CreateAsync(
        CreateReusableContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes an item's editorial metadata.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The members being changed; anything omitted is left alone.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The item as it now stands, or a conflict when the save lost a race.</returns>
    Task<CmsResult<ReusableContentDetail>> PatchAsync(
        int id,
        PatchReusableContentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Writes a payload to the item's draft.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="request">The payload, and the row version the caller last saw.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The saved draft, every schema problem found, or a conflict carrying what is stored.</returns>
    Task<CmsResult<ReusableDraftSaveResult>> SaveDraftAsync(
        int id,
        SaveDraftRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Resets the draft to a copy of what is published.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The reset draft, or an invalid result when nothing has ever been published.</returns>
    Task<CmsResult<ReusableDraftState>> DiscardDraftAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Lists an item's version history, newest first.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>Every version row, or a not-found result.</returns>
    Task<CmsResult<IReadOnlyList<ReusableVersionSummary>>> ListVersionsAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the publish checks, and reports what a publish would change, without publishing.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>What a publish would find, or a not-found result.</returns>
    /// <remarks>
    /// This is what the confirmation dialog of spec section 9.4 is built from, which is why the
    /// impact list is on the <em>check</em> and not only on the publish: a count reported after the
    /// irreversible part is a receipt, not a confirmation.
    /// </remarks>
    Task<CmsResult<ReusablePublishValidation>> ValidateAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Publishes the current draft.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="acknowledgeWarnings">
    /// Whether the caller has seen the non-blocking diagnostics — the blast radius among them — and
    /// still wants to proceed. False turns a warning into a refusal carrying it.
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What the publish did, including the pages it changed.</returns>
    Task<CmsResult<ReusablePublishResult>> PublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>Retires the item, so every placement of it renders nothing.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="acknowledgeWarnings">Whether the caller has seen what will stop rendering.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The version retired and the pages it was on, or an invalid result when nothing was live.</returns>
    Task<CmsResult<ReusableUnpublishResult>> UnpublishAsync(
        int id,
        bool acknowledgeWarnings = false,
        CancellationToken cancellationToken = default);

    /// <summary>Moves the item to the recycle bin.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// What was deleted, or a conflict carrying the where-used list when stored content still places
    /// the item. Refused rather than cascaded: a deleted item is invisible to the resolver, so
    /// deleting one that is still placed blanks a zone on every page holding it, discovered by a
    /// visitor (spec section 9.4).
    /// </returns>
    Task<CmsResult<ReusableDeleteResult>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Restores an item from the recycle bin.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The restored item. It comes back <em>unpublished</em> whatever it was before, for the reason
    /// a restored page does: bringing content live again is a publish somebody performs, not a side
    /// effect of undoing a delete (spec section 14.10).
    /// </returns>
    Task<CmsResult<ReusableContentDetail>> RestoreAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Answers where the item is used.</summary>
    /// <param name="id">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The pages and items placing it, with exact counts.</returns>
    Task<CmsResult<ReferenceImpact>> WhereUsedAsync(int id, CancellationToken cancellationToken = default);
}
