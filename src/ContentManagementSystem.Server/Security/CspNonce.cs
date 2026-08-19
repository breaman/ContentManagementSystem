using System.Buffers.Text;
using System.Security.Cryptography;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// The per-request nonce the backoffice policy is written around
/// ([`ADR-0013`](../../../docs/adr/0013-backoffice-editor-bundle-and-style-nonce.md),
/// [`ADR-0026`](../../../docs/adr/0026-three-content-security-policies-public-carries-no-nonce.md),
/// tasks P6-08 and P9-01).
/// </summary>
/// <remarks>
/// Three things quote this one value, and they have to agree or the page breaks: the
/// <c>Content-Security-Policy</c> header, the <c>&lt;script type="importmap"&gt;</c> element Blazor
/// renders into the host page, and the <c>&lt;style&gt;</c> element CodeMirror injects at runtime.
/// <strong>CodeMirror 6 ships no stylesheet.</strong> It builds its theme in JavaScript, which a
/// strict <c>style-src</c> treats as inline and blocks — and the editor then renders as an unstyled
/// <c>&lt;div&gt;</c> with no exception, no failed request, and no console error. Spike S3 proved
/// this with a control experiment; it is the single most important finding it returned.
/// <para>
/// The nonce is generated on the server and consumed by a library running in the browser, so the
/// <c>&lt;meta name="csp-nonce"&gt;</c> tag the host page emits is part of the contract between them
/// rather than an implementation detail either side may drop.
/// </para>
/// <para>
/// One value per request, from a cryptographic source. A nonce reused across requests is not a
/// nonce — it is a constant an attacker who has read one page can quote — and a nonce from
/// <see cref="Random"/> is a constant anyone can predict. That property is also why the public
/// policy carries no nonce at all: public responses are cached and replayed, so a nonce in one
/// would be exactly the constant this paragraph rules out (ADR-0026).
/// </para>
/// </remarks>
public interface ICspNonce
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
public sealed class CspNonce : ICspNonce
{
    private readonly Lazy<string> _value = new(Generate);

    /// <inheritdoc />
    /// <remarks>
    /// Generated on first read rather than on construction. Most requests this service is resolved
    /// for — every API call, every media rendition — are served under the public policy, which has
    /// no nonce in it and never asks for one.
    /// </remarks>
    public string Value => _value.Value;

    /// <summary>
    /// Sixteen random bytes, base64<em>url</em>-encoded.
    /// </summary>
    /// <remarks>
    /// The URL alphabet rather than the standard one, and it is not cosmetic. A nonce is matched by
    /// the browser as an exact string between the header and the attribute, and standard base64's
    /// <c>+</c> comes back out of the Razor attribute encoder as <c>&amp;#x2B;</c> — which browsers
    /// do decode, so the match holds, but only because of a step that has nothing to do with
    /// security and could stop happening. <c>-</c> and <c>_</c> are in the CSP grammar's
    /// <c>base64-value</c> and survive HTML encoding untouched, so the two copies are the same bytes
    /// in the source as well as after parsing.
    /// </remarks>
    private static string Generate() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
}
