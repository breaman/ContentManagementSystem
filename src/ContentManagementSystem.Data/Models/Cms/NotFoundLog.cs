namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// One URL that was asked for and did not resolve, with a count of how often.
/// </summary>
/// <remarks>
/// Spec section 10.6 calls the report built on this table the single highest-value artefact of a
/// site migration: it is the difference between guessing which legacy URLs mattered and reading the
/// list, sorted by traffic, with a "create redirect" button beside each row.
/// <para>
/// One row per URL, not one per request — the row is upserted and <see cref="HitCount"/>
/// incremented. That is what keeps a crawler hammering a dead URL from turning the table into the
/// site's largest. It is also why the table is exempt from audit capture (spec section 23.5): every
/// 404 on the site would otherwise write an <c>AuditLog</c> row as well.
/// </para>
/// </remarks>
public class NotFoundLog : EntityBase
{
    /// <summary>The unresolved URL, normalized by <c>SiteUrls.Normalize</c>.</summary>
    public string Url { get; set; } = null!;

    /// <summary>SHA-256 of <see cref="Url"/>, carrying the unique index (spec section 23.5).</summary>
    public byte[] UrlHash { get; set; } = null!;

    /// <summary>
    /// Where the request came from, when the browser said. Null when it did not.
    /// </summary>
    /// <remarks>
    /// The most recent referrer rather than all of them, because the question this answers is "who
    /// is still linking to this" and one live example is enough to go and ask them. Keeping every
    /// distinct referrer would make the table unbounded again by a different route.
    /// </remarks>
    public string? Referrer { get; set; }

    /// <summary>How many requests this URL has received. A <c>bigint</c> for the same reason as on a redirect.</summary>
    public long HitCount { get; set; }

    /// <summary>When the URL was first requested.</summary>
    public DateTimeOffset FirstSeenOn { get; set; }

    /// <summary>When the URL was most recently requested.</summary>
    public DateTimeOffset LastSeenOn { get; set; }
}
