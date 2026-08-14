using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Creates and reads pages, and edits the metadata around their content (task P2-07).
/// </summary>
/// <remarks>
/// Every method authorizes the caller itself, for the reason <c>ITemplateService</c> gives: the
/// endpoint policy is the door and this is the lock, and a service reached from a CLI verb or a
/// hosted job is subject to the same rules (spec section 20.4).
/// <para>
/// The boundary with the rest of Phase 2 is what a page <em>is</em> versus what happens to it. This
/// service owns the row and its draft's metadata; the payload belongs to <c>DraftService</c>, the
/// lifecycle to <c>PublishingService</c> and <c>RecycleBinService</c>, and the tree position to
/// <c>PageTreeService</c>. Nothing here can change a page's status, template, or parent — accepting
/// any of those as ordinary metadata is the mass-assignment hole spec section 20.1 names.
/// </para>
/// </remarks>
public interface IPageService
{
    /// <summary>
    /// Creates a page from a template, together with the draft version it starts life with.
    /// </summary>
    /// <param name="request">The page to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The created page, an invalid result naming every rule the request broke, a conflict when a
    /// sibling already uses the slug, or a not-found result when the template or parent is gone.
    /// </returns>
    /// <remarks>
    /// The draft's payload is empty and schema-valid: every zone is <em>absent</em> rather than
    /// present-and-null, which is what "never authored" means, and a required zone blocks only a
    /// publish (spec section 8.3). The page and its draft are inserted in one transaction, since a
    /// page whose <c>DraftVersionId</c> was never set is a page no editor can open and no query
    /// reports as broken.
    /// </remarks>
    Task<CmsResult<PageDetail>> CreateAsync(
        CreatePageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Reads a page's metadata and its draft payload.</summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The page, or a not-found result.</returns>
    Task<CmsResult<PageDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a partial update to a page's title, slug, SEO, and editorial metadata.
    /// </summary>
    /// <param name="id">Identity of the page.</param>
    /// <param name="request">The members to change. Omitted members are left alone.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated page, a not-found result, an invalid result, or a conflict.</returns>
    /// <remarks>
    /// Mutates the draft version in place and creates no new version row (acceptance criterion
    /// P2 #2). The published version is not touched, so a title corrected in the backoffice does not
    /// reach the public site until someone publishes.
    /// </remarks>
    Task<CmsResult<PageDetail>> PatchMetadataAsync(
        int id,
        PatchPageMetadataRequest request,
        CancellationToken cancellationToken = default);
}
