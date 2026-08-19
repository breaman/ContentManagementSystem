namespace ContentManagementSystem.Core.Delivery.Seo;

/// <summary>
/// The absolute address the site is being served at, for the places that must emit one
/// (spec sections 18.2 and 18.3).
/// </summary>
/// <remarks>
/// Canonical links, Open Graph URLs, JSON-LD, and the sitemap are all read by machines that resolve
/// nothing relative to the page they came from — a canonical link has to be absolute or it is
/// ignored. Everything else in the CMS stores and compares site-relative URLs, so this is the one
/// seam where the host is introduced, and it is an abstraction rather than an <c>HttpContext</c>
/// read so that the sitemap can also be produced by a background job.
/// </remarks>
public interface ISiteAddress
{
    /// <summary>The site's base address, always with a trailing slash.</summary>
    Uri BaseUri { get; }

    /// <summary>
    /// Makes a site-relative URL absolute, leaving one that already is alone.
    /// </summary>
    /// <param name="url">A site-relative URL such as <c>/about</c>, or an absolute one.</param>
    /// <returns>The absolute URL.</returns>
    /// <remarks>
    /// An editor may type an absolute canonical URL pointing at another site — that is what the
    /// field is for on a page syndicated from elsewhere — so an already-absolute value is passed
    /// through rather than mangled onto this host.
    /// <para>
    /// "Absolute" here means <em>http or https</em>, not whatever <see cref="Uri"/> will parse. On a
    /// Unix host <c>/about</c> is a perfectly good absolute <c>file:</c> URI, so the obvious
    /// <c>TryCreate(url, UriKind.Absolute, …)</c> test passes for every site-relative URL the CMS
    /// stores, and the page's canonical address comes out as <c>file:///about</c>.
    /// </para>
    /// </remarks>
    public string Absolute(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return BaseUri.ToString();

        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        return new Uri(BaseUri, url.TrimStart('/')).ToString();
    }
}
