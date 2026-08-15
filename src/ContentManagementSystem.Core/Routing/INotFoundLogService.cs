namespace ContentManagementSystem.Core.Routing;

/// <summary>
/// Records the URLs that were asked for and did not resolve (spec section 10.6).
/// </summary>
/// <remarks>
/// The report built on this table is, per the spec, the single highest-value artefact of a site
/// migration: it is the difference between guessing which legacy URLs mattered and reading the list
/// sorted by traffic, with a "create redirect" action beside each row.
/// <para>
/// One row per URL, upserted, not one row per request. A crawler hammering a dead address would
/// otherwise make this the largest table on the site, which is also why the table is exempt from
/// audit capture (spec section 23.5).
/// </para>
/// </remarks>
public interface INotFoundLogService
{
    /// <summary>
    /// Counts one unresolved request.
    /// </summary>
    /// <param name="url">The URL as requested; normalized before it is stored.</param>
    /// <param name="referrer">Where the request came from, when the browser said.</param>
    /// <param name="cancellationToken">Token observed while writing.</param>
    /// <remarks>
    /// Never throws. Housekeeping must not be the reason a visitor gets something other than the
    /// 404 page they were about to get anyway — a failure here is logged and swallowed, exactly as
    /// counting a redirect hit is.
    /// </remarks>
    Task RecordAsync(string? url, string? referrer, CancellationToken cancellationToken = default);
}
