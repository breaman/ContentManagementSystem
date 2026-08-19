namespace ContentManagementSystem.Core.Delivery.Seo;

/// <summary>
/// Turns a loaded page version into the document head it is served with (spec section 18.2).
/// </summary>
/// <remarks>
/// A service rather than logic inside the head component, for two reasons. It reads the database —
/// site settings, the ancestor trail the breadcrumb list is built from, the media item the share
/// image is rendered from — and a component that queried while rendering would make the head's
/// correctness depend on render order. And every fallback rule in spec section 18.1 is here, where
/// it can be asserted without rendering anything at all.
/// </remarks>
public interface ISeoMetadataBuilder
{
    /// <summary>
    /// Builds the head metadata for one version.
    /// </summary>
    /// <param name="content">The version being rendered.</param>
    /// <param name="isPreview">
    /// Whether this render is for an editor rather than the public. A preview is served
    /// <c>noindex, nofollow</c> whatever the page says, because a preview URL that reached an index
    /// would publish a draft.
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The resolved head.</returns>
    Task<SeoMetadata> BuildAsync(
        PublishedContent content,
        bool isPreview = false,
        CancellationToken cancellationToken = default);
}
