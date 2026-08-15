namespace ContentManagementSystem.Shared.Contracts.Routing;

/// <summary>
/// Body of <c>POST /api/cms/v1/redirects</c>.
/// </summary>
/// <param name="FromUrl">
/// The URL being redirected away from. Normalized on the way in, so <c>/About/</c> and <c>/about</c>
/// are the same row rather than two that shadow each other.
/// </param>
/// <param name="ToPageId">
/// Destination expressed as a page, so the redirect follows that page's future URL changes. Preferred
/// over <paramref name="ToUrl"/> for anything internal (decision D6).
/// </param>
/// <param name="ToUrl">
/// Literal destination. The only option for an external target; for an internal one it freezes the
/// redirect to a URL that stops being correct the first time the target moves.
/// </param>
/// <param name="StatusCode">301 permanent or 302 temporary. Defaults to 301.</param>
/// <param name="Notes">Why the redirect exists. Housekeeping only.</param>
/// <remarks>
/// Exactly one of <paramref name="ToPageId"/> and <paramref name="ToUrl"/> is supplied. Both is
/// refused rather than resolved by precedence: a request carrying two destinations was built by
/// something that does not know which one it means.
/// </remarks>
public sealed record CreateRedirectRequest(
    string? FromUrl,
    int? ToPageId = null,
    string? ToUrl = null,
    short StatusCode = 301,
    string? Notes = null);

/// <summary>
/// Body of <c>PATCH /api/cms/v1/redirects/{id}</c>.
/// </summary>
/// <param name="ToPageId">New page destination, or null to leave it alone.</param>
/// <param name="ToUrl">New literal destination, or null to leave it alone.</param>
/// <param name="StatusCode">New status code, or null to leave it alone.</param>
/// <param name="IsEnabled">Whether the redirect is served, or null to leave it alone.</param>
/// <param name="Notes">New note, or null to leave it alone.</param>
/// <remarks>
/// <c>FromUrl</c> is absent on purpose. A redirect <em>is</em> its source URL — changing it is
/// deleting one rule and creating another, and an edit that quietly did both would leave the
/// original URL serving nothing with no record that it ever did.
/// </remarks>
public sealed record UpdateRedirectRequest(
    int? ToPageId = null,
    string? ToUrl = null,
    short? StatusCode = null,
    bool? IsEnabled = null,
    string? Notes = null);

/// <summary>One redirect, as the management API reports it.</summary>
/// <param name="Id">Identity of the redirect.</param>
/// <param name="FromUrl">The normalized source URL.</param>
/// <param name="ToUrl">Literal destination, or null when the destination is a page.</param>
/// <param name="ToPageId">Page destination, or null when the destination is literal.</param>
/// <param name="ResolvedToUrl">
/// Where the redirect actually sends a visitor right now. Equal to <paramref name="ToUrl"/> for a
/// literal destination and to the page's current URL otherwise — which is the whole point of storing
/// a page id, and is not derivable by a client holding only the row.
/// </param>
/// <param name="StatusCode">301 or 302.</param>
/// <param name="IsAutomatic">Whether a URL change created this, as opposed to a person.</param>
/// <param name="IsEnabled">Whether the redirect is served.</param>
/// <param name="Notes">Why the redirect exists.</param>
/// <param name="HitCount">How many times it has been followed.</param>
/// <param name="LastHitOn">When it was last followed, or null if never.</param>
public sealed record RedirectDetail(
    int Id,
    string FromUrl,
    string? ToUrl,
    int? ToPageId,
    string? ResolvedToUrl,
    short StatusCode,
    bool IsAutomatic,
    bool IsEnabled,
    string? Notes,
    long HitCount,
    DateTimeOffset? LastHitOn);

/// <summary>
/// What a CSV import did.
/// </summary>
/// <param name="Created">Rows that became new redirects.</param>
/// <param name="Updated">Rows that replaced the destination of an existing automatic redirect.</param>
/// <param name="Skipped">
/// Rows that were not imported. Each one has a warning in the diagnostics naming its line number —
/// a legacy list is thousands of rows long and always has a few bad ones, and refusing the file
/// leaves the operator editing a spreadsheet with no report of what was wrong.
/// </param>
public sealed record RedirectImportResult(int Created, int Updated, int Skipped);
