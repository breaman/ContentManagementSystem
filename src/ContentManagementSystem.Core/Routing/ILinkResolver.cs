namespace ContentManagementSystem.Core.Routing;

/// <summary>
/// Where a stored page reference actually points, at the moment it is rendered.
/// </summary>
/// <param name="PageId">The page the content named.</param>
/// <param name="Url">Its current URL, or null when it has none that the audience may see.</param>
/// <param name="IsPublished">Whether the target is live.</param>
/// <param name="Title">The target's current title, for a link that carries no text of its own.</param>
/// <remarks>
/// <see cref="IsPublished"/> is not the same question as whether <see cref="Url"/> is null. Inside
/// preview an unpublished target resolves to its draft URL and is badged; on the public site the
/// same target resolves to nothing at all. Collapsing the two would either leak an unpublished URL
/// onto the public page or make preview useless for walking an unreleased section
/// (spec section 12.3).
/// </remarks>
public sealed record ResolvedLink(int PageId, string? Url, bool IsPublished, string? Title);

/// <summary>
/// Turns the page ids stored in content into the URLs a page is rendered with.
/// </summary>
/// <remarks>
/// This is the other half of decision D6. Storing a page id rather than a URL is only worth
/// anything because something resolves it late — a link authored two years ago points at the right
/// place after its target has moved twice, and nothing had to go back and rewrite the payload
/// (acceptance criterion P3 #7).
/// <para>
/// Batched deliberately. A page with a related-articles list and a navigation block resolves dozens
/// of ids, and a resolver called once per link is the classic N+1 that turns a fast page into a slow
/// one only under real content.
/// </para>
/// </remarks>
public interface ILinkResolver
{
    /// <summary>
    /// Resolves every page id a render is about to need.
    /// </summary>
    /// <param name="pageIds">The ids, in any order and with any repeats.</param>
    /// <param name="includeUnpublished">
    /// Whether an unpublished target resolves to its draft URL. True only inside preview; the public
    /// delivery path passes false so a draft URL cannot reach an anonymous visitor.
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>One entry per distinct id that names a page that still exists.</returns>
    /// <remarks>
    /// An id with no entry in the result is a page that was deleted or never existed. Delivery
    /// renders such a link as plain text and logs, rather than throwing or emitting a dead href
    /// (spec section 15.3).
    /// </remarks>
    Task<IReadOnlyDictionary<int, ResolvedLink>> ResolveAsync(
        IEnumerable<int> pageIds,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default);
}
