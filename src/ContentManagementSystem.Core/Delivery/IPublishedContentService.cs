namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// Loads the version an anonymous visitor is allowed to see (spec sections 5.4 and 20.1).
/// </summary>
/// <remarks>
/// The read half of the delivery path, and the only place the public site reaches content. It is
/// deliberately narrow: there is one method, it takes a page id, and it answers with the
/// <em>published</em> version or with nothing at all.
/// <para>
/// <strong>The published filter is in the query, not in a check afterwards.</strong> Spec
/// section 20.1 names an unpublished page reaching an anonymous request as the threat this whole
/// layer exists to prevent, and a service that loaded a page and then decided which version to hand
/// back has already put the draft in memory next to the response. A filter that runs at the data
/// layer cannot be forgotten by a later caller, and a caller who wants a specific version — preview,
/// in P3-16 — has to ask a different service that says so in its name.
/// </para>
/// <para>
/// Route resolution is <c>IRouteResolver</c>'s, not this interface's. It already owns the ordering
/// that makes a live page outrank a redirect at the same URL (spec section 10.5), and a second entry
/// point into it here would be a second copy of that decision.
/// </para>
/// </remarks>
public interface IPublishedContentService
{
    /// <summary>
    /// Loads a page's published version and deserializes its content.
    /// </summary>
    /// <param name="pageId">The page, as resolved from the request URL.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>
    /// The content to render, or <see langword="null"/> when the page does not exist, is in the
    /// recycle bin, or has never been published. All three are the same answer to a visitor — a 404 —
    /// and distinguishing them in the return type would invite a caller to leak the difference.
    /// </returns>
    Task<PublishedContent?> GetAsync(int pageId, CancellationToken cancellationToken = default);
}
