using System.Security.Cryptography;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The per-request style nonce the backoffice hands to CodeMirror
/// ([`ADR-0013`](../../../docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md), task P6-08).
/// </summary>
/// <remarks>
/// <strong>CodeMirror 6 ships no stylesheet.</strong> It injects a <c>&lt;style&gt;</c> element at
/// runtime, which a strict <c>style-src</c> treats as inline and blocks — and the editor then renders
/// as an unstyled <c>&lt;div&gt;</c> with no exception, no failed request, and no console error.
/// Spike S3 proved this with a control experiment; it is the single most important finding it
/// returned.
/// <para>
/// The nonce is generated on the server and consumed by a library running in the browser, so the
/// <c>&lt;meta name="csp-nonce"&gt;</c> tag the host page emits is part of the contract between them
/// rather than an implementation detail either side may drop.
/// </para>
/// <para>
/// One value per request, from a cryptographic source. A nonce reused across requests is not a
/// nonce — it is a constant an attacker who has read one page can quote — and a nonce from
/// <see cref="Random"/> is a constant anyone can predict.
/// </para>
/// </remarks>
public interface IStyleNonce
{
    /// <summary>The nonce for the request being served, Base64-encoded.</summary>
    string Value { get; }
}

/// <inheritdoc />
/// <remarks>
/// Registered scoped, which is what makes "per request" true: a singleton would hand every visitor
/// the same value for the lifetime of the process.
/// <para>
/// 128 bits, which is what the CSP specification recommends and comfortably more than the 64 it sets
/// as the floor.
/// </para>
/// </remarks>
public sealed class StyleNonce : IStyleNonce
{
    private readonly Lazy<string> _value = new(Generate);

    /// <inheritdoc />
    /// <remarks>
    /// Generated on first read rather than on construction. Most requests this service is resolved
    /// for — every API call, every media rendition — never render a host page and never need one.
    /// </remarks>
    public string Value => _value.Value;

    private static string Generate() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
}
