using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Tags;

/// <summary>
/// The site's tag vocabulary and what carries it (task P8-20, spec sections 14.7 and 17.1).
/// </summary>
/// <remarks>
/// <strong>Tags are editorial metadata on the page, not content in its payload.</strong> Spec section
/// 14.7 puts them beside owner, review date, and internal notes — facts about the page rather than
/// things rendered on it — so they are written through the metadata patch and stored as
/// <c>PageTag</c> rows. The <c>tags</c> field type remains what it always was: a content field whose
/// values a template may render, indexed for search like any other text. Having one writer for the
/// taxonomy is the point: two would mean a tag removed on the properties panel reappearing the next
/// time the payload was saved.
/// </remarks>
public interface ITagService
{
    /// <summary>Lists every tag with the number of pages carrying it.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<CmsResult<IReadOnlyList<TagSummary>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Suggests tags for what an editor has typed so far.
    /// </summary>
    /// <param name="prefix">What they have typed. Empty offers the most-used tags.</param>
    /// <param name="limit">Most suggestions to return.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <remarks>
    /// Autocomplete against the existing vocabulary is what stops a tag list becoming "product",
    /// "Products", and "product-page" — three labels for one idea, each filtering to a different
    /// third of the site.
    /// </remarks>
    Task<CmsResult<IReadOnlyList<TagSummary>>> SuggestAsync(
        string? prefix,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a tag everywhere it is used, merging it into an existing tag if the name is taken.
    /// </summary>
    /// <param name="id">The tag being renamed.</param>
    /// <param name="request">The new label.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    Task<CmsResult<RenameTagResult>> RenameAsync(
        int id,
        RenameTagRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes a tag and takes it off every page carrying it.</summary>
    /// <param name="id">The tag.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>How many pages lost the tag.</returns>
    Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the tags on one page, inside the caller's transaction.
    /// </summary>
    /// <param name="pageId">The page.</param>
    /// <param name="tags">The labels it should carry, in any order and with any casing.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The labels the page ends up carrying, normalized and de-duplicated.</returns>
    /// <remarks>
    /// Nothing here calls <c>SaveChanges</c>, for the reason the reference projector gives: these
    /// rows belong to the same transaction as the metadata change that caused them, and tags that
    /// committed while the patch beside them rolled back would describe an edit that never happened.
    /// <para>
    /// A label naming no existing tag creates one. Two editors introducing the <em>same</em> new tag
    /// in the same instant race on the unique slug, and the loser's save fails and is retried by the
    /// client — a narrow, self-correcting window that is cheaper than serializing every tagged save
    /// behind a lock.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<string>> ApplyAsync(
        int pageId,
        IReadOnlyList<string>? tags,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the tags one page carries.</summary>
    /// <param name="pageId">The page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<IReadOnlyList<string>> ForPageAsync(int pageId, CancellationToken cancellationToken = default);
}
