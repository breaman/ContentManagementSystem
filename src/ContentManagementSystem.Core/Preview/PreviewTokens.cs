using System.Buffers.Text;
using System.Security.Cryptography;

namespace ContentManagementSystem.Core.Preview;

/// <summary>
/// Generates and hashes shareable preview secrets (task P3-17, spec section 12.2).
/// </summary>
/// <remarks>
/// One place, so the three facts that have to agree — how many bytes, how they are encoded, and what
/// exactly is hashed — cannot be restated differently by the issuing path and the redeeming path. A
/// mismatch there produces a link that is issued successfully and then never works, and the token is
/// unrecoverable so there is nothing to compare against while debugging it.
/// <para>
/// <strong>The hash is taken over the decoded bytes, not the encoded string.</strong> Base64url has
/// spellings that differ as text and decode identically — trailing padding, and for a 32-byte value
/// the final character carries unused bits — so hashing the string would make one secret hash to
/// several values and the lookup would depend on which spelling a mail client passed through.
/// </para>
/// </remarks>
public static class PreviewTokens
{
    /// <summary>Length of the secret in bytes (spec section 12.2).</summary>
    /// <remarks>
    /// 256 bits of CSPRNG output. That is why a single unsalted SHA-256 is the right hash for the
    /// stored copy: there is no dictionary to run against a value drawn uniformly from this space,
    /// so a work factor would only add cost to every request that presents a link.
    /// </remarks>
    public const int TokenBytes = 32;

    /// <summary>Default lifetime of a preview link (spec section 12.2).</summary>
    public const int DefaultExpiryDays = 7;

    /// <summary>Longest lifetime a preview link may be issued for (spec section 12.2).</summary>
    public const int MaxExpiryDays = 30;

    /// <summary>The site-relative path a shared link is served at.</summary>
    public const string SharePathPrefix = "/preview/s/";

    /// <summary>
    /// Creates a new secret and the hash to store beside it.
    /// </summary>
    /// <returns>The base64url secret to hand to the caller once, and its SHA-256 hash.</returns>
    /// <example>
    /// <code>
    /// var (token, hash) = PreviewTokens.Create();
    /// // token goes into the response; only hash is ever written to the database.
    /// </code>
    /// </example>
    public static (string Token, byte[] Hash) Create()
    {
        Span<byte> secret = stackalloc byte[TokenBytes];

        RandomNumberGenerator.Fill(secret);

        return (Base64Url.EncodeToString(secret), SHA256.HashData(secret));
    }

    /// <summary>
    /// Hashes a presented token so it can be looked up.
    /// </summary>
    /// <param name="token">The base64url secret from the URL.</param>
    /// <param name="hash">The SHA-256 hash to look up, when the token was well-formed.</param>
    /// <returns>Whether the token was even the right shape to have been issued by this code.</returns>
    /// <remarks>
    /// Shape is checked before the database is asked, which is the cheap half of the rate limit: a
    /// crawler walking <c>/preview/s/{anything}</c> is answered without a query. It also means the
    /// length rule lives here rather than being an implicit consequence of nothing matching.
    /// </remarks>
    public static bool TryHash(string? token, out byte[] hash)
    {
        hash = [];

        if (string.IsNullOrEmpty(token)) return false;

        Span<byte> decoded = stackalloc byte[TokenBytes];

        // OperationStatus.Done with exactly TokenBytes written is the only acceptable answer: a
        // shorter value that decodes cleanly was not issued here, and a longer one overflows.
        if (Base64Url.DecodeFromChars(token, decoded, out _, out var written) is not
                System.Buffers.OperationStatus.Done ||
            written != TokenBytes)
        {
            return false;
        }

        hash = SHA256.HashData(decoded);

        return true;
    }

    /// <summary>The URL a freshly issued token is shared at.</summary>
    /// <param name="token">The base64url secret.</param>
    public static string UrlFor(string token) => SharePathPrefix + token;
}
