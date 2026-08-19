using System.Globalization;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Delivery.Seo;

/// <inheritdoc cref="ISeoMetadataBuilder" />
/// <param name="context">The application database context.</param>
/// <param name="media">Resolves the share image's id to the item it names.</param>
/// <param name="signer">Signs the share image's rendition URL.</param>
/// <param name="site">The absolute address every emitted URL is made against.</param>
/// <remarks>
/// Two reads per page, both no-tracking: the site settings row, and the page's published ancestors
/// for the breadcrumb list. Both are on the request path a visitor waits for, which is affordable
/// because the whole response is what the output cache stores (spec section 16.1) — the head is
/// rebuilt when the page is, not when it is served.
/// </remarks>
public sealed class SeoMetadataBuilder(
    ApplicationDbContext context,
    IMediaResolver media,
    IMediaUrlSigner signer,
    ISiteAddress site) : ISeoMetadataBuilder
{
    /// <inheritdoc />
    public async Task<SeoMetadata> BuildAsync(
        PublishedContent content,
        bool isPreview = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var settings = await context.SiteSettings
            .AsNoTracking()
            .Select(row => new SiteFacts(
                row.SiteName,
                row.HomePageId,
                row.DefaultOgImageMediaId,
                row.GoogleSiteVerification))
            .FirstOrDefaultAsync(cancellationToken) ?? SiteFacts.Unconfigured;

        var canonical = site.Absolute(content.CanonicalOrOwnUrl);
        var title = content.DocumentTitle;
        var description = Trimmed(content.Seo.MetaDescription);
        var image = await ShareImageAsync(content, settings, cancellationToken);
        var isHome = settings.HomePageId == content.PageId || content.Url == SiteUrls.Root;

        IReadOnlyList<Ancestor> trail = isHome
            ? []
            : await TrailAsync(content, cancellationToken);

        var meta = new List<SeoMetaTag>(16);

        AddOpenGraph(meta, content, settings, canonical, title, description, image);
        AddTwitter(meta, content, title, description, image);

        if (Trimmed(settings.GoogleSiteVerification) is { } verification)
        {
            meta.Add(SeoMetaTag.Named("google-site-verification", verification));
        }

        return new SeoMetadata(
            title,
            description,
            canonical,
            Robots(content, isPreview),
            meta,
            StructuredData(content, settings, canonical, title, description, image, trail, isHome),
            image?.MediaId);
    }

    /// <summary>The <c>robots</c> directive pair, as the meta element spells it.</summary>
    /// <remarks>
    /// Always emitted, including the permissive case. An absent meta element and an explicit "yes"
    /// mean the same thing to a crawler but not to a person auditing why a page is missing from the
    /// index, and the cost of saying so is thirty bytes.
    /// </remarks>
    private static string Robots(PublishedContent content, bool isPreview)
    {
        if (isPreview) return SeoMetadata.NoIndexNoFollow;

        var index = content.Seo.RobotsIndex ? "index" : "noindex";
        var follow = content.Seo.RobotsFollow ? "follow" : "nofollow";

        return $"{index}, {follow}";
    }

    private static void AddOpenGraph(
        List<SeoMetaTag> meta,
        PublishedContent content,
        SiteFacts settings,
        string canonical,
        string title,
        string? description,
        ShareImage? image)
    {
        // og:type decides how a crawler files the page, and "website" is the value the protocol
        // gives for a page that is not one of its richer types. A page whose editor chose "article"
        // also gets its publish timestamp, which is what a card renders a date from.
        var type = Trimmed(content.Seo.OgType) ?? "website";

        meta.Add(SeoMetaTag.Property("og:type", type));
        meta.Add(SeoMetaTag.Property("og:title", Trimmed(content.Seo.OgTitle) ?? title));
        meta.Add(SeoMetaTag.Property("og:url", canonical));
        if (Trimmed(settings.SiteName) is { } siteName)
        {
            meta.Add(SeoMetaTag.Property("og:site_name", siteName));
        }

        if ((Trimmed(content.Seo.OgDescription) ?? description) is { Length: > 0 } summary)
        {
            meta.Add(SeoMetaTag.Property("og:description", summary));
        }

        if (image is not null)
        {
            meta.Add(SeoMetaTag.Property("og:image", image.Url));
            meta.Add(SeoMetaTag.Property("og:image:width", RenditionSpec.SocialWidth.ToString(CultureInfo.InvariantCulture)));
            meta.Add(SeoMetaTag.Property("og:image:height", RenditionSpec.SocialHeight.ToString(CultureInfo.InvariantCulture)));

            if (image.AltText is { Length: > 0 } alt)
            {
                meta.Add(SeoMetaTag.Property("og:image:alt", alt));
            }
        }

        if (string.Equals(type, "article", StringComparison.OrdinalIgnoreCase) &&
            content.PublishedOn is { } published)
        {
            meta.Add(SeoMetaTag.Property("article:published_time", published.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));

            if (content.ModifiedOn is { } modified)
            {
                meta.Add(SeoMetaTag.Property("article:modified_time", modified.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)));
            }
        }
    }

    private static void AddTwitter(
        List<SeoMetaTag> meta,
        PublishedContent content,
        string title,
        string? description,
        ShareImage? image)
    {
        // The card type is chosen from what actually resolved rather than from what was configured:
        // a page declaring summary_large_image with no image renders as an empty box on the network
        // that trusts it.
        var card = Trimmed(content.Seo.TwitterCard) ?? (image is null ? "summary" : "summary_large_image");

        if (image is null && string.Equals(card, "summary_large_image", StringComparison.OrdinalIgnoreCase))
        {
            card = "summary";
        }

        meta.Add(SeoMetaTag.Named("twitter:card", card));
        meta.Add(SeoMetaTag.Named("twitter:title", Trimmed(content.Seo.OgTitle) ?? title));

        if ((Trimmed(content.Seo.OgDescription) ?? description) is { Length: > 0 } summary)
        {
            meta.Add(SeoMetaTag.Named("twitter:description", summary));
        }

        if (image is not null)
        {
            meta.Add(SeoMetaTag.Named("twitter:image", image.Url));

            if (image.AltText is { Length: > 0 } alt)
            {
                meta.Add(SeoMetaTag.Named("twitter:image:alt", alt));
            }
        }
    }

    /// <summary>
    /// The structured-data documents this page emits, or the editor's own when they wrote some.
    /// </summary>
    /// <remarks>
    /// A hand-authored document <em>replaces</em> the generated set rather than joining it. Two
    /// descriptions of the same page, one of them written by somebody who could not see the other,
    /// is how a site ends up with two conflicting <c>WebPage</c> nodes at one URL — and an editor
    /// who filled that field in did so because the generated answer was wrong (spec section 18.2).
    /// </remarks>
    private List<string> StructuredData(
        PublishedContent content,
        SiteFacts settings,
        string canonical,
        string title,
        string? description,
        ShareImage? image,
        IReadOnlyList<Ancestor> trail,
        bool isHome)
    {
        if (JsonLd.Normalize(content.Seo.StructuredDataJson) is { } authored) return [authored];

        var documents = new List<string>(4);
        var home = site.BaseUri.ToString();

        // The host is a poor name for a site and a better one than the empty string, which is what
        // an unwritten settings row would otherwise put in front of every crawler.
        var siteName = Trimmed(settings.SiteName) ?? site.BaseUri.Host;

        if (isHome)
        {
            var website = JsonLd.Document("WebSite");
            website["name"] = siteName;
            website["url"] = home;
            documents.Add(JsonLd.Serialize(website));

            var organization = JsonLd.Document("Organization");
            organization["name"] = siteName;
            organization["url"] = home;

            if (image is not null) organization["logo"] = image.Url;

            documents.Add(JsonLd.Serialize(organization));
        }

        // Article rather than WebPage only when the editor said so through og:type. Guessing from
        // the template would be wrong on the first site whose "article" template is a landing page.
        var page = JsonLd.Document(
            string.Equals(Trimmed(content.Seo.OgType), "article", StringComparison.OrdinalIgnoreCase)
                ? "Article"
                : "WebPage");

        page["name"] = title;
        page["headline"] = title;
        page["url"] = canonical;

        if (description is { Length: > 0 }) page["description"] = description;
        if (image is not null) page["image"] = image.Url;
        if (content.PublishedOn is { } published) page["datePublished"] = published.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        if (content.ModifiedOn is { } modified) page["dateModified"] = modified.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

        page["isPartOf"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["@type"] = "WebSite",
            ["name"] = siteName,
            ["url"] = home,
        };

        documents.Add(JsonLd.Serialize(page));

        if (trail.Count > 0)
        {
            var items = new List<object?>(trail.Count + 1);
            var position = 1;

            foreach (var ancestor in trail)
            {
                items.Add(BreadcrumbItem(position++, ancestor.Title, site.Absolute(ancestor.Url)));
            }

            items.Add(BreadcrumbItem(position, title, canonical));

            var breadcrumbs = JsonLd.Document("BreadcrumbList");
            breadcrumbs["itemListElement"] = items;
            documents.Add(JsonLd.Serialize(breadcrumbs));
        }

        return documents;
    }

    private static Dictionary<string, object?> BreadcrumbItem(int position, string name, string url) =>
        new(StringComparer.Ordinal)
        {
            ["@type"] = "ListItem",
            ["position"] = position,
            ["name"] = name,
            ["item"] = url,
        };

    /// <summary>
    /// The page's published ancestors, root first.
    /// </summary>
    /// <remarks>
    /// Read from the materialized path in one query rather than by following <c>ParentId</c> a row
    /// at a time — the column exists for exactly this (spec section 10.1). Unpublished ancestors are
    /// left out rather than emitted without a link: a breadcrumb naming a page the visitor cannot
    /// reach is worse than a shorter trail, and a crawler is entitled to follow every item.
    /// </remarks>
    private async Task<IReadOnlyList<Ancestor>> TrailAsync(
        PublishedContent content,
        CancellationToken cancellationToken)
    {
        var ancestors = await context.Pages
            .AsNoTracking()
            .Where(page => page.Id == content.PageId)
            .SelectMany(
                page => context.Pages.Where(candidate =>
                    candidate.Id != page.Id &&
                    candidate.PublishedVersionId != null &&
                    page.Path.StartsWith(candidate.Path)),
                (page, candidate) => new Ancestor(
                    candidate.Depth,
                    candidate.PublishedVersion!.Title,
                    context.PageRoutes
                        .Where(route => route.PageId == candidate.Id && route.IsPublished)
                        .OrderByDescending(route => route.IsPrimary)
                        .Select(route => route.Url)
                        .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return [.. ancestors
            .Where(ancestor => ancestor.Url is { Length: > 0 })
            .OrderBy(ancestor => ancestor.Depth)];
    }

    /// <summary>
    /// Resolves the page's share image to a signed 1200x630 crop, or nothing.
    /// </summary>
    /// <remarks>
    /// Cropped rather than contained, and at the ratio every network states, because the alternative
    /// is letting each of them crop it for itself: a portrait photograph rendered untouched becomes
    /// a centre strip on one network and a letterboxed thumbnail on another. JPEG rather than WebP
    /// even though the site serves WebP: the crawlers that fetch this image are not browsers, and
    /// several of them still decode nothing else.
    /// </remarks>
    private async Task<ShareImage?> ShareImageAsync(
        PublishedContent content,
        SiteFacts settings,
        CancellationToken cancellationToken)
    {
        if ((content.Seo.OgImageMediaId ?? settings.DefaultOgImageMediaId) is not { } mediaId) return null;

        var resolved = await media.ResolveAsync([mediaId], cancellationToken);

        if (!resolved.TryGetValue(mediaId, out var item)) return null;
        if (item.Kind is not MediaKind.Image) return null;
        if (ResponsiveImages.FallbackFormat(item.ContentType) is null) return null;

        var url = signer.BuildUrl(
            new RenditionSpec(
                item.Id,
                RenditionSpec.SocialWidth,
                RenditionSpec.SocialHeight,
                RenditionMode.Crop,
                ImageOutputFormat.Jpeg,
                RenditionSpec.DefaultQuality,
                item.EditsVersion,
                item.Edits.FocalPoint),
            item.OriginalFileName);

        // Absolute, unlike every other image the site emits. A share image is fetched by a crawler
        // that has only the tag, with no document to resolve a relative URL against.
        return new ShareImage(item.Id, site.Absolute(url), Trimmed(item.AltText));
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>The site-wide facts the head needs, projected out of the settings row.</summary>
    private sealed record SiteFacts(
        string SiteName,
        int? HomePageId,
        int? DefaultOgImageMediaId,
        string? GoogleSiteVerification)
    {
        /// <summary>What a site whose settings row has not been written yet reports.</summary>
        public static SiteFacts Unconfigured { get; } = new(string.Empty, null, null, null);
    }

    /// <summary>One published ancestor, for the breadcrumb list.</summary>
    private sealed record Ancestor(int Depth, string Title, string? Url);

    /// <summary>The resolved share image.</summary>
    private sealed record ShareImage(int MediaId, string Url, string? AltText);
}
