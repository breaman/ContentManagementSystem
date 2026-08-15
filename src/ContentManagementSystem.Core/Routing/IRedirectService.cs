using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Routing;

namespace ContentManagementSystem.Core.Routing;

/// <summary>
/// Where a redirect sends a visitor, once the chain has been followed.
/// </summary>
/// <param name="RedirectId">Identity of the first redirect in the chain, whose hit count is counted.</param>
/// <param name="TargetUrl">The final destination, after following any chain.</param>
/// <param name="StatusCode">Status the first redirect in the chain asked for.</param>
/// <remarks>
/// The status comes from the <em>first</em> hop and the URL from the last. A chain that starts
/// permanent and passes through a temporary hop is still permanently leaving the URL the visitor
/// asked for, which is the only thing the status code describes.
/// </remarks>
public sealed record RedirectMatch(int RedirectId, string TargetUrl, short StatusCode);

/// <summary>
/// Owns the <c>Redirect</c> table: creation, chain flattening, loop refusal, and resolution.
/// </summary>
/// <remarks>
/// Gap #2 in spec section 10.5. Reorganising a site is the ordinary case, not the exception, and a
/// CMS without this turns every reorganisation into a wave of 404s that nobody notices until search
/// traffic drops a month later.
/// <para>
/// Two rules do most of the work here and are worth stating before reading any method.
/// <strong>Chains are flattened on write</strong>, so <c>A → B</c> followed by <c>B → C</c> leaves
/// <c>A → C</c> stored, not a chain to walk — a visitor gets one round trip rather than three, and
/// depth cannot grow without bound. <strong>A live page always outranks a redirect</strong> at the
/// same URL, which is enforced by <see cref="IRouteResolver"/> asking the routes first; without it,
/// retiring a page and later reusing its URL would be impossible.
/// </para>
/// </remarks>
public interface IRedirectService
{
    /// <summary>
    /// Deepest chain followed before giving up, at write time and at resolve time alike.
    /// </summary>
    /// <remarks>
    /// Ten, from spec section 10.5. Flattening means a legitimate chain is never longer than one,
    /// so reaching this bound is a sign that something built a cycle the write-time check did not
    /// see — a promoted environment, a direct database edit, a page whose URL changed into another
    /// redirect's source.
    /// </remarks>
    const int MaxChainDepth = 10;

    /// <summary>
    /// Finds where a URL should be sent, following and flattening any chain.
    /// </summary>
    /// <param name="url">The requested URL. Normalized before lookup.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The destination, or null when no enabled redirect claims the URL.</returns>
    /// <remarks>
    /// Returns null rather than a partial answer when a cycle is found. Serving the last hop before
    /// the loop closed would send a visitor somewhere arbitrary; a 404 at least tells the truth, and
    /// the cycle is logged for whoever has to fix it.
    /// </remarks>
    Task<RedirectMatch?> ResolveAsync(string? url, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts a followed redirect.
    /// </summary>
    /// <param name="redirectId">Identity of the redirect that was followed.</param>
    /// <param name="cancellationToken">Token observed while writing.</param>
    /// <remarks>
    /// A single unconditional <c>UPDATE</c> rather than a read-modify-write, so concurrent hits on
    /// the same row add up instead of overwriting each other. Best-effort by design: a redirect must
    /// never be slower or less reliable than the page it points at, so a failure here is logged and
    /// swallowed.
    /// </remarks>
    Task RecordHitAsync(int redirectId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the automatic redirect that a URL change leaves behind.
    /// </summary>
    /// <param name="fromUrl">The URL the page is vacating.</param>
    /// <param name="toPageId">The page that moved.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>Whether a redirect was added or an existing automatic one was repointed.</returns>
    /// <remarks>
    /// Not authorized and not saved: this is called from inside <see cref="IUrlService"/>'s subtree
    /// rebuild, which runs in the caller's transaction and has already established that the caller
    /// may move the page. Saving here would commit half a move.
    /// <para>
    /// A <em>manual</em> redirect already occupying the source URL is left exactly as it is
    /// (spec section 10.5): somebody made a decision about that URL, and a tree move is not an
    /// argument against it.
    /// </para>
    /// </remarks>
    Task<bool> RecordAutomaticAsync(
        string fromUrl,
        int toPageId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists redirects, newest first.</summary>
    /// <param name="search">Substring matched against the source and destination URLs, or null for all.</param>
    /// <param name="cursor">Opaque paging token from a previous page, or null for the first.</param>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    Task<CmsResult<CursorPage<RedirectDetail>>> ListAsync(
        string? search = null,
        string? cursor = null,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a hand-entered redirect.</summary>
    /// <param name="request">Source, destination, and status.</param>
    /// <param name="cancellationToken">Token observed while querying and saving.</param>
    Task<CmsResult<RedirectDetail>> CreateAsync(
        CreateRedirectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Changes a redirect's destination, status, or enabled state.</summary>
    /// <param name="id">Identity of the redirect.</param>
    /// <param name="request">The members to change.</param>
    /// <param name="cancellationToken">Token observed while querying and saving.</param>
    Task<CmsResult<RedirectDetail>> UpdateAsync(
        int id,
        UpdateRedirectRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a redirect outright.</summary>
    /// <param name="id">Identity of the redirect.</param>
    /// <param name="cancellationToken">Token observed while querying and saving.</param>
    /// <remarks>
    /// A hard delete, unlike a page. A redirect carries no content and no history worth preserving,
    /// and the reason to remove one is almost always that it has a zero hit count — the row is
    /// noise, and soft-deleting noise leaves it in the way of the unique index on its source URL.
    /// </remarks>
    Task<CmsResult<int>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Imports redirects from a CSV document.</summary>
    /// <param name="csv">The document: a header row, then <c>from,to,status,notes</c>.</param>
    /// <param name="cancellationToken">Token observed while querying and saving.</param>
    /// <returns>Counts, with a warning per skipped row naming its line number.</returns>
    /// <remarks>
    /// A row whose source URL already has a redirect replaces it, hand-entered rules included. The
    /// operator uploading a file is stating what these URLs should do with the same authority as the
    /// person who typed the row, and the alternative — protecting manual rows — would mean
    /// <see cref="ExportAsync"/>'s output could not be fed back in.
    /// <para>
    /// Every imported row is marked manual, so a later tree move leaves it alone.
    /// </para>
    /// </remarks>
    Task<CmsResult<RedirectImportResult>> ImportAsync(
        string csv,
        CancellationToken cancellationToken = default);

    /// <summary>Exports every redirect as CSV, in the shape <see cref="ImportAsync"/> reads.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <remarks>
    /// Round-trippable on purpose. The realistic way a large legacy list gets cleaned up is export,
    /// edit in a spreadsheet, re-import — and an export that its own importer cannot read makes that
    /// a manual retype.
    /// </remarks>
    Task<CmsResult<string>> ExportAsync(CancellationToken cancellationToken = default);
}
