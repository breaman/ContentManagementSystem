using System.Net;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Api.Cms;

/// <summary>
/// The <c>ETag</c> / <c>If-Match</c> half of the API's optimistic concurrency (task P2-20).
/// </summary>
/// <remarks>
/// A page version's <c>rowversion</c> is already the authority on whether a write lost a race — the
/// database arbitrates it inside the <c>UPDATE</c> predicate. This class does nothing more than
/// carry that value over HTTP in the header the protocol reserves for it, so that a client can use
/// ordinary conditional-request machinery instead of a bespoke member in every request body.
/// <para>
/// <strong>A mismatch answers 409, not 412.</strong> That is a deliberate departure from the letter
/// of RFC 9110: a 412 is conventionally bodiless, and the losing editor needs the winning draft in
/// its hands to offer keep-mine / take-theirs / open-diff (spec section 11.8, acceptance criterion
/// P2 #8). Re-fetching it in a second round trip would race exactly the same way.
/// </para>
/// </remarks>
public static class ETags
{
    /// <summary>
    /// Wraps a Base64 <c>rowversion</c> as a strong entity tag.
    /// </summary>
    /// <param name="rowVersion">The token, as the service reports it.</param>
    /// <returns>The quoted tag, or null when there is no version to tag.</returns>
    /// <remarks>
    /// Strong rather than weak: two representations sharing a <c>rowversion</c> are the same bytes,
    /// not merely equivalent, which is what a strong comparison means and what a conditional write
    /// requires.
    /// </remarks>
    public static string? Format(string? rowVersion) =>
        string.IsNullOrEmpty(rowVersion) ? null : $"\"{rowVersion}\"";

    /// <summary>
    /// Stamps a response with the entity tag of the version it carries.
    /// </summary>
    /// <param name="httpContext">The request being answered.</param>
    /// <param name="rowVersion">The Base64 <c>rowversion</c> of the representation.</param>
    public static void Stamp(HttpContext httpContext, string? rowVersion)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (Format(rowVersion) is { } tag) httpContext.Response.Headers.ETag = tag;
    }

    /// <summary>
    /// Reads the <c>rowversion</c> a conditional request claims to have last seen.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <returns>
    /// The unquoted Base64 token, or null when the request carried no usable precondition —
    /// including <c>If-Match: *</c>, which asserts only that the resource exists and says nothing
    /// about which version the caller holds.
    /// </returns>
    public static string? IfMatch(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var header = httpContext.Request.Headers.IfMatch;

        if (header.Count == 0) return null;

        foreach (var value in header)
        {
            if (!EntityTagHeaderValue.TryParse(value, out var tag)) continue;

            // "*" is a precondition on existence, which the route's own lookup already covers.
            if (tag.Tag == "*") continue;

            var raw = tag.Tag.Value;

            if (string.IsNullOrEmpty(raw)) continue;

            return raw.Trim('"');
        }

        return null;
    }

    /// <summary>
    /// Resolves the expected <c>rowversion</c> for a write that must be conditional.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="fromBody">The value the request body carried, if any.</param>
    /// <param name="expected">The token the write should be guarded by.</param>
    /// <returns>The refusal to return, or null when <paramref name="expected"/> is usable.</returns>
    /// <remarks>
    /// The header wins when both are present, because the header is what a caching intermediary and
    /// a generic HTTP client understand; the body member stays supported because a client that has
    /// only ever spoken JSON should not have to learn conditional requests to save a draft.
    /// <para>
    /// Supplying neither is refused with <c>428 Precondition Required</c> rather than accepted as an
    /// unconditional write. An editor that forgot the header would otherwise silently overwrite a
    /// colleague, which is the one failure the whole draft model exists to prevent — and 428 exists
    /// precisely so a server can insist.
    /// </para>
    /// </remarks>
    public static IResult? RequireIfMatch(HttpContext httpContext, string? fromBody, out string expected)
    {
        expected = IfMatch(httpContext) ?? fromBody ?? string.Empty;

        if (expected.Length > 0) return null;

        return CmsProblems.Problem(
            HttpStatusCode.PreconditionRequired,
            "precondition-required",
            "Precondition required",
            ValidationResult.Error(
                PageCodes.ConcurrentChange,
                "This write has to say which version it is replacing. Send the ETag from the last " +
                "read back as an If-Match header."));
    }
}
