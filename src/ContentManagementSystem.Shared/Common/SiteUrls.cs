using System.Security.Cryptography;
using System.Text;

namespace ContentManagementSystem.Shared.Common;

/// <summary>
/// The canonical form of a site-relative URL, and the hash that carries its unique index
/// (spec sections 10.3 and 23.5).
/// </summary>
/// <remarks>
/// Two separate jobs live here because they must not be done separately. A <see cref="Hash"/> taken
/// over an unnormalized string indexes a URL nobody will ever ask for: <c>/About/</c> and
/// <c>/about</c> would hash differently, occupy two rows, and defeat the unique index that exists to
/// stop exactly that. <see cref="Hash"/> therefore normalizes first, and no caller is offered a way
/// to skip it.
/// <para>
/// The hash exists because <c>nvarchar(2000)</c> is 4000 bytes and a SQL Server index key stops at
/// 900. Uniqueness is enforced on <c>binary(32)</c>; the URL column stays for display and for
/// <c>LIKE</c> prefix queries, which do not need the key.
/// </para>
/// <para>
/// SHA-256 is used as a collision-resistant identity function, not as a security barrier — a URL is
/// public by definition, so there is nothing here to keep secret. What the choice does buy is that
/// an attacker cannot manufacture two URLs that collide and make one page's route resolve to
/// another's.
/// </para>
/// </remarks>
public static class SiteUrls
{
    /// <summary>The home page's URL. Exactly one page has it (spec section 10.3).</summary>
    public const string Root = "/";

    /// <summary>Length in bytes of the value <see cref="Hash"/> returns.</summary>
    public const int HashLength = 32;

    /// <summary>
    /// Puts a site-relative URL into the one form the database stores and the resolver looks up.
    /// </summary>
    /// <param name="url">The URL as supplied — from an editor, a request path, or a CSV import.</param>
    /// <returns>
    /// A lowercase, leading-slash, no-trailing-slash, NFC-normalized path. An empty or whitespace
    /// input becomes <see cref="Root"/>.
    /// </returns>
    /// <remarks>
    /// Query strings and fragments are <em>not</em> stripped here, because this normalizes a stored
    /// route and a route never has one — the delivery endpoint hands over the path alone. Stripping
    /// them would silently accept a redirect row whose author meant the query to matter.
    /// </remarks>
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Root;

        var trimmed = url.Trim();

        // A URL arriving from a request path is already percent-decoded by the framework; one typed
        // into the redirect editor may not be. Decoding here means the two agree, and it is what
        // makes spec section 10.3's "stored percent-decoded" true of every writer rather than of
        // the ones that remembered.
        trimmed = Uri.UnescapeDataString(trimmed);

        var normalized = trimmed.ToLowerInvariant().Normalize(NormalizationForm.FormC);

        if (!normalized.StartsWith('/')) normalized = '/' + normalized;

        // The trailing slash is dropped rather than appended, matching the routing configuration
        // the application already runs with. Root is the one URL that keeps its slash, since
        // dropping it would leave the empty string.
        normalized = normalized.TrimEnd('/');

        return normalized.Length == 0 ? Root : normalized;
    }

    /// <summary>
    /// Hashes a URL into the fixed-width value its unique index is built on.
    /// </summary>
    /// <param name="url">The URL, normalized or not — <see cref="Normalize"/> is applied either way.</param>
    /// <returns>32 bytes of SHA-256 over the UTF-8 encoding of the normalized URL.</returns>
    public static byte[] Hash(string? url) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(url)));

    /// <summary>
    /// Joins an ancestor URL and a slug into a child's URL.
    /// </summary>
    /// <param name="parentUrl">The parent's full URL, or null for a page at the root.</param>
    /// <param name="slug">The child's URL segment.</param>
    /// <returns>The normalized child URL.</returns>
    public static string Combine(string? parentUrl, string slug)
    {
        var parent = Normalize(parentUrl);

        return Normalize(parent == Root ? Root + slug : parent + "/" + slug);
    }

    /// <summary>
    /// Whether one URL is an ancestor of, or equal to, another.
    /// </summary>
    /// <param name="ancestorUrl">The candidate ancestor.</param>
    /// <param name="url">The URL being tested.</param>
    /// <remarks>
    /// Segment-aware on purpose. A plain prefix test says <c>/new</c> contains <c>/news</c>, which
    /// on a redirect loop check is the difference between refusing a legitimate row and accepting a
    /// cycle.
    /// </remarks>
    public static bool IsSelfOrDescendant(string? ancestorUrl, string? url)
    {
        var ancestor = Normalize(ancestorUrl);
        var candidate = Normalize(url);

        if (ancestor == Root) return true;

        return candidate == ancestor || candidate.StartsWith(ancestor + "/", StringComparison.Ordinal);
    }
}
