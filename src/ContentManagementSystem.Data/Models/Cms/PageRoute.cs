namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A materialized URL for a page: the row an incoming request is resolved against.
/// </summary>
/// <remarks>
/// Routes are stored rather than computed, so resolving a request is one indexed lookup instead of
/// walking the page's ancestors on every hit (spec section 10.4). The cost is that a move or a slug
/// change has to recompute the routes of the page <em>and every descendant</em>, which
/// <c>UrlService</c> does in a single transaction.
/// <para>
/// A page has more than one route only in the sense that its draft and published URLs may differ:
/// <see cref="IsPublished"/> separates them, and the unique index applies to the published ones
/// alone. That is what lets a draft-only page have a resolvable URL for preview without claiming it
/// publicly.
/// </para>
/// </remarks>
public class PageRoute : EntityBase
{
    /// <summary>Page this URL reaches.</summary>
    public int PageId { get; set; }

    /// <summary>Page this URL reaches.</summary>
    public Page Page { get; set; } = null!;

    /// <summary>The full site-relative URL, normalized by <c>SiteUrls.Normalize</c>.</summary>
    public string Url { get; set; } = null!;

    /// <summary>
    /// SHA-256 of <see cref="Url"/>, carrying the unique index.
    /// </summary>
    /// <remarks>
    /// <c>nvarchar(2000)</c> is 4000 bytes and a SQL Server index key stops at 900, so uniqueness
    /// is enforced on the hash (spec section 23.5). Nothing writes this by hand — it is derived
    /// from <see cref="Url"/>, and a row where the two disagree is a URL that can never be resolved.
    /// </remarks>
    public byte[] UrlHash { get; set; } = null!;

    /// <summary>
    /// Whether this is the URL the page calls its own, as opposed to an alias.
    /// </summary>
    /// <remarks>
    /// Canonical link tags, sitemap entries, and the URL rendered for an internal link all read the
    /// primary route. v1 creates exactly one route per publish state, so this is always set; it
    /// exists now because a table that later grows aliases with no way to say which one is canonical
    /// is a migration nobody wants to write against live routes.
    /// </remarks>
    public bool IsPrimary { get; set; } = true;

    /// <summary>
    /// Whether this route is publicly resolvable, or exists only so preview can address the page.
    /// </summary>
    /// <remarks>
    /// The filtered unique index applies only when this is set, so an unpublished page's URL may sit
    /// alongside a live page already occupying it — which is exactly what happens while an editor
    /// prepares a replacement for a page that is still serving traffic.
    /// </remarks>
    public bool IsPublished { get; set; }

    /// <summary>When the route was materialized.</summary>
    public DateTimeOffset CreatedOn { get; set; }
}
