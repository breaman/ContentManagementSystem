namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// A standing instruction to send one URL to another.
/// </summary>
/// <remarks>
/// Gap #2 in spec section 10.5. Rows are created automatically whenever a published page's URL
/// changes — for the page and every descendant — so that reorganising the tree does not silently
/// break every inbound link and search result pointing at the old shape.
/// <para>
/// <strong>A live page always wins over a redirect at the same URL.</strong> The resolver asks
/// <see cref="PageRoute"/> first and only falls through to here. Without that rule, retiring a page
/// and later reusing its URL for new content would be impossible: the redirect the retirement
/// created would outrank the page.
/// </para>
/// </remarks>
public class Redirect : FingerPrintEntityBase
{
    /// <summary>The URL being redirected away from, normalized by <c>SiteUrls.Normalize</c>.</summary>
    public string FromUrl { get; set; } = null!;

    /// <summary>SHA-256 of <see cref="FromUrl"/>, carrying the unique index (spec section 23.5).</summary>
    public byte[] FromUrlHash { get; set; } = null!;

    /// <summary>
    /// Literal destination, used when <see cref="ToPageId"/> is null.
    /// </summary>
    /// <remarks>
    /// The only option for an external destination, and the fallback for an internal one whose page
    /// is not known. A site-relative internal target should use <see cref="ToPageId"/> instead — see
    /// the remarks there.
    /// </remarks>
    public string? ToUrl { get; set; }

    /// <summary>
    /// Destination expressed as a page, so the redirect follows that page's future URL changes.
    /// </summary>
    /// <remarks>
    /// Preferred over <see cref="ToUrl"/> for internal targets and for the same reason internal
    /// links store a page id rather than a URL string (decision D6): a redirect frozen to a literal
    /// URL becomes a redirect to a 404 the first time its target moves, and nothing reports it.
    /// </remarks>
    public int? ToPageId { get; set; }

    /// <summary>Destination expressed as a page.</summary>
    public Page? ToPage { get; set; }

    /// <summary>HTTP status to answer with: 301 permanent or 302 temporary.</summary>
    public short StatusCode { get; set; } = 301;

    /// <summary>
    /// Whether the system created this on a URL change, as opposed to an administrator entering it.
    /// </summary>
    /// <remarks>
    /// A manual redirect overrides an automatic one on conflict (spec section 10.5): a person who
    /// typed a destination has made a decision that a subsequent tree move must not silently
    /// overwrite.
    /// </remarks>
    public bool IsAutomatic { get; set; }

    /// <summary>Whether the redirect is served. Cleared to retire a rule without losing its history.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Why this redirect exists. Housekeeping only.</summary>
    public string? Notes { get; set; }

    /// <summary>
    /// How many times the redirect has been followed.
    /// </summary>
    /// <remarks>
    /// A <c>bigint</c> because a redirect on a busy legacy URL outlives the site's own page views,
    /// and because the entire value of the column is telling an administrator which of a thousand
    /// imported rows can be pruned. Counted on a best-effort background write; a redirect must not
    /// be slower than the page it points at.
    /// </remarks>
    public long HitCount { get; set; }

    /// <summary>When the redirect was last followed. Null until the first hit.</summary>
    public DateTimeOffset? LastHitOn { get; set; }
}
