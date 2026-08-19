namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// The search and social metadata one page version carries (spec section 18.1).
/// </summary>
/// <remarks>
/// Grouped rather than spread across <see cref="PublishedContent"/> because these eleven values are
/// read by exactly one consumer — the document head — and never individually by anything else. A
/// renderer that wanted <c>TwitterCard</c> would be doing something wrong; keeping them behind one
/// member makes that visible instead of leaving eleven more properties in reach of every caller.
/// <para>
/// Every value is stored as the editor left it, including the empty ones. The fallbacks — a missing
/// meta title becoming the page title, a missing Open Graph description becoming the meta
/// description — are applied when the head is built, not here, so that renaming a page still moves
/// its social title unless somebody deliberately overrode it.
/// </para>
/// </remarks>
/// <param name="MetaTitle">Overrides the document title; null falls back to the page title.</param>
/// <param name="MetaDescription">Meta description for the document head.</param>
/// <param name="CanonicalUrl">Explicit canonical URL; null falls back to the page's own URL.</param>
/// <param name="RobotsIndex">Whether search engines may index the page.</param>
/// <param name="RobotsFollow">Whether search engines may follow links out of the page.</param>
/// <param name="OgTitle">Open Graph title; null falls back to the document title.</param>
/// <param name="OgDescription">Open Graph description; null falls back to the meta description.</param>
/// <param name="OgImageMediaId">Media item shown when the page is shared; null uses the site default.</param>
/// <param name="OgType">Open Graph object type; null is rendered as <c>website</c>.</param>
/// <param name="TwitterCard">Twitter card type; null is chosen from whether an image resolved.</param>
/// <param name="StructuredDataJson">Hand-authored JSON-LD, which replaces the generated documents.</param>
public sealed record PublishedSeo(
    string? MetaTitle,
    string? MetaDescription,
    string? CanonicalUrl,
    bool RobotsIndex,
    bool RobotsFollow,
    string? OgTitle,
    string? OgDescription,
    int? OgImageMediaId,
    string? OgType,
    string? TwitterCard,
    string? StructuredDataJson)
{
    /// <summary>The metadata of a page whose SEO panel nobody has touched.</summary>
    /// <remarks>
    /// Indexable and followable, matching the column defaults on <c>PageVersion</c>. A default that
    /// said <c>noindex</c> would be the safer-looking choice and the wrong one: it would hide a site
    /// from search engines because of a record nobody filled in.
    /// </remarks>
    public static PublishedSeo Default { get; } = new(
        MetaTitle: null,
        MetaDescription: null,
        CanonicalUrl: null,
        RobotsIndex: true,
        RobotsFollow: true,
        OgTitle: null,
        OgDescription: null,
        OgImageMediaId: null,
        OgType: null,
        TwitterCard: null,
        StructuredDataJson: null);
}
