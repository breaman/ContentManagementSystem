namespace ContentManagementSystem.Core.Delivery.Seo;

/// <summary>
/// One meta element the document head emits.
/// </summary>
/// <param name="Attribute">
/// Which attribute names it: <c>property</c> for Open Graph, <c>name</c> for everything else.
/// </param>
/// <param name="Key">The name or property value, such as <c>og:title</c>.</param>
/// <param name="Content">The content attribute's value, already unescaped.</param>
/// <remarks>
/// Open Graph is RDFa and spells its key as <c>property</c>; Twitter's cards and the classic
/// <c>description</c> use <c>name</c>. Facebook's scraper tolerates the wrong one and several
/// validators do not, so which attribute a tag uses is carried rather than guessed at render time.
/// </remarks>
public sealed record SeoMetaTag(string Attribute, string Key, string Content)
{
    /// <summary>The attribute name Open Graph tags use.</summary>
    public const string PropertyAttribute = "property";

    /// <summary>The attribute name every other meta tag uses.</summary>
    public const string NameAttribute = "name";

    /// <summary>Builds an Open Graph tag.</summary>
    /// <param name="key">The property, such as <c>og:title</c>.</param>
    /// <param name="content">Its value.</param>
    /// <returns>The tag.</returns>
    public static SeoMetaTag Property(string key, string content) =>
        new(PropertyAttribute, key, content);

    /// <summary>Builds a named meta tag.</summary>
    /// <param name="key">The name, such as <c>twitter:card</c>.</param>
    /// <param name="content">Its value.</param>
    /// <returns>The tag.</returns>
    public static SeoMetaTag Named(string key, string content) => new(NameAttribute, key, content);
}

/// <summary>
/// Everything the document head of one page emits, resolved and ready to write (spec section 18.2).
/// </summary>
/// <param name="Title">The <c>&lt;title&gt;</c> element's text.</param>
/// <param name="Description">Meta description, or null when the page has none.</param>
/// <param name="CanonicalUrl">Absolute canonical URL.</param>
/// <param name="Robots">The <c>robots</c> directive pair, as the meta element spells it.</param>
/// <param name="Meta">Open Graph, Twitter, and verification tags, in emission order.</param>
/// <param name="JsonLd">JSON-LD documents, each already serialized.</param>
/// <param name="OgImageMediaId">
/// The media item the social image was rendered from, or null when none resolved. Carried so the
/// render can take a <c>media:{id}</c> cache dependency on it — a page whose share image was
/// replaced in the library has to be re-rendered like any other page showing it.
/// </param>
/// <param name="Language">
/// What the document's <c>lang</c> attribute says, from <c>SiteSettings.Culture</c> (spec section 28,
/// task P9-10). Positional and undefaulted, so a new construction site has to say what the page is
/// written in rather than inheriting a guess.
/// </param>
/// <remarks>
/// <c>Language</c> is here rather than passed to the document separately because it is resolved from
/// the same settings row as everything else in this record, on the one read the head already makes.
/// A screen reader chooses its pronunciation from it, so a page whose <c>lang</c> is wrong is read
/// aloud in the wrong accent — a failure nobody sees and every listener hears.
/// </remarks>
/// <remarks>
/// A resolved value object rather than a bag of raw fields: every fallback in spec section 18.1 has
/// already been applied, every URL is absolute, and the JSON-LD is text. That is what lets the
/// component that writes it be a dumb loop, and lets the fallbacks be unit tested without rendering
/// anything.
/// </remarks>
public sealed record SeoMetadata(
    string Title,
    string? Description,
    string CanonicalUrl,
    string Robots,
    IReadOnlyList<SeoMetaTag> Meta,
    IReadOnlyList<string> JsonLd,
    int? OgImageMediaId,
    string Language)
{
    /// <summary>
    /// What <c>lang</c> says when the settings row has not been written yet.
    /// </summary>
    /// <remarks>
    /// The same default <c>SiteSettings.Culture</c> carries. An absent <c>lang</c> would be worse
    /// than a wrong one — a screen reader with nothing to go on uses the listener's own locale, so a
    /// page with no attribute is read in whatever accent happens to be configured.
    /// </remarks>
    public const string DefaultLanguage = "en-US";

    /// <summary>The <c>robots</c> content for a page that may be indexed and followed.</summary>
    public const string IndexFollow = "index, follow";

    /// <summary>The <c>robots</c> content for a page no crawler may index or follow.</summary>
    public const string NoIndexNoFollow = "noindex, nofollow";

    /// <summary>
    /// The head of a document rendered outside any site context, such as a component test.
    /// </summary>
    public static SeoMetadata Empty { get; } = new(
        Title: string.Empty,
        Description: null,
        CanonicalUrl: string.Empty,
        Robots: NoIndexNoFollow,
        Meta: [],
        JsonLd: [],
        OgImageMediaId: null,
        Language: DefaultLanguage);
}
