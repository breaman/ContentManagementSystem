using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Media.Processing;

using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media.Delivery;

/// <summary>
/// Signs and validates rendition URLs (tasks P5-14 and P5-18, spec section 13.5).
/// </summary>
/// <remarks>
/// The signature is what makes an image endpoint safe to expose. Without it,
/// <c>/media/1/1x1</c> through <c>/media/1/9999x9999</c> is an unbounded encode farm reachable by
/// anyone with a browser — the classic image-resizer denial of service, where a few hundred requests
/// pin every core and fill the disk (ADR 0007).
/// <para>
/// Signatures are produced server-side while rendering markup, so an editor never sees one and no
/// client ever has to construct one.
/// </para>
/// </remarks>
public interface IMediaUrlSigner
{
    /// <summary>
    /// Signs a rendition spec.
    /// </summary>
    /// <param name="spec">The rendition being linked to.</param>
    /// <param name="issuedOn">When the URL is being issued, for lifetime-limited deployments.</param>
    /// <returns>The signature to place in the <c>s</c> parameter.</returns>
    string Sign(RenditionSpec spec, DateTimeOffset? issuedOn = null);

    /// <summary>
    /// Checks a presented signature.
    /// </summary>
    /// <param name="spec">The spec parsed from the request.</param>
    /// <param name="signature">The presented <c>s</c> parameter.</param>
    /// <param name="issuedOn">The presented issue time, when the deployment signs one.</param>
    /// <returns><see langword="true"/> when the signature was produced by a key still in service.</returns>
    bool Validate(RenditionSpec spec, string? signature, DateTimeOffset? issuedOn = null);

    /// <summary>Builds the complete, signed, site-relative URL for a rendition.</summary>
    /// <param name="spec">The rendition to link to.</param>
    /// <param name="name">A display name for the file, for the sake of readable URLs and downloads.</param>
    /// <returns>The URL to emit.</returns>
    string BuildUrl(RenditionSpec spec, string name);

    /// <summary>Signs a link to a stored original.</summary>
    /// <param name="mediaItemId">The item.</param>
    /// <param name="editsVersion">Its edits generation, so a revert changes the URL.</param>
    /// <returns>The signature.</returns>
    string SignOriginal(int mediaItemId, int editsVersion);

    /// <summary>Checks a presented signature for a stored original.</summary>
    /// <param name="mediaItemId">The item.</param>
    /// <param name="editsVersion">Its current edits generation.</param>
    /// <param name="signature">The presented signature.</param>
    /// <returns><see langword="true"/> when the signature was produced by a key still in service.</returns>
    bool ValidateOriginal(int mediaItemId, int editsVersion, string? signature);

    /// <summary>Builds the complete, signed, site-relative URL for a stored original.</summary>
    /// <param name="mediaItemId">The item.</param>
    /// <param name="editsVersion">Its edits generation.</param>
    /// <param name="name">A display name for the file.</param>
    /// <returns>The URL to emit.</returns>
    string BuildOriginalUrl(int mediaItemId, int editsVersion, string name);
}

/// <inheritdoc cref="IMediaUrlSigner" />
public sealed class MediaUrlSigner : IMediaUrlSigner
{
    /// <summary>Path prefix every rendition is served under.</summary>
    public const string PathPrefix = "/media";

    /// <summary>Query parameter carrying the signature.</summary>
    public const string SignatureParameter = "s";

    /// <summary>Query parameter carrying the issue time, when one is signed.</summary>
    public const string IssuedParameter = "t";

    private readonly byte[] _key;
    private readonly byte[]? _previousKey;
    private readonly MediaSigningOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Creates the signer, deriving a development key when none is configured.
    /// </summary>
    /// <param name="options">The configured keys.</param>
    /// <param name="clock">Source of the current time, for the rotation grace period.</param>
    /// <param name="logger">Logger.</param>
    /// <remarks>
    /// A missing key generates a random one and logs loudly rather than throwing. Throwing would
    /// stop a developer's first run for a secret they have no reason to have configured yet; the
    /// generated key is per-process, so a deployment that scales out and ignored the warning finds
    /// out immediately — instance A's URLs do not validate on instance B.
    /// </remarks>
    public MediaUrlSigner(MediaSigningOptions options, TimeProvider clock, ILogger<MediaUrlSigner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _clock = clock;

        if (TryDecodeKey(options.Key, out var key))
        {
            _key = key;
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(MediaSigningOptions.MinimumKeyBytes);

            logger.LogWarning(
                "No media signing key is configured, so a per-process key was generated. Rendition " +
                "URLs will not validate across instances or across restarts. Configure {Section}:{Key}.",
                MediaSigningOptions.SectionName,
                nameof(MediaSigningOptions.Key));
        }

        _previousKey = TryDecodeKey(options.PreviousKey, out var previous) ? previous : null;
    }

    /// <inheritdoc />
    public string Sign(RenditionSpec spec, DateTimeOffset? issuedOn = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        return Compute(_key, spec, issuedOn);
    }

    /// <inheritdoc />
    public bool Validate(RenditionSpec spec, string? signature, DateTimeOffset? issuedOn = null)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrEmpty(signature)) return false;

        if (_options.SignatureLifetime is { } lifetime)
        {
            // Only enforced when a deployment asked for it; the default is indefinite, because a
            // rendition URL is immutable public content and expiring it breaks cached pages.
            if (issuedOn is null || _clock.GetUtcNow() - issuedOn > lifetime) return false;
        }

        if (FixedTimeEquals(Compute(_key, spec, issuedOn), signature)) return true;

        // The grace period. A URL signed with the key being retired keeps working until the
        // configured moment, so a rotation does not break every cached page at once
        // (spec section 20.8).
        if (_previousKey is null) return false;

        if (_options.PreviousKeyExpiresOn is { } expiry && _clock.GetUtcNow() > expiry) return false;

        return FixedTimeEquals(Compute(_previousKey, spec, issuedOn), signature);
    }

    /// <inheritdoc />
    public string BuildUrl(RenditionSpec spec, string name)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var issuedOn = _options.SignatureLifetime is null ? (DateTimeOffset?)null : _clock.GetUtcNow();
        var builder = new StringBuilder(160);

        builder.Append(PathPrefix).Append('/')
            .Append(spec.MediaItemId).Append('/')
            .Append(spec.Width).Append('x').Append(spec.Height).Append('/')
            .Append(spec.Mode.ToString().ToLowerInvariant()).Append('/')
            .Append(Uri.EscapeDataString(Slug(name))).Append('.').Append(spec.Extension);

        // The parameters that are not in the path. The edits version is in here rather than in the
        // path because it changes without the picture being different — folding it in is what makes
        // one library edit change every URL on the site (ADR 0007).
        builder.Append("?v=").Append(spec.EditsVersion);

        if (spec.Quality != RenditionSpec.DefaultQuality) builder.Append("&q=").Append(spec.Quality);

        if (spec.FocalPoint is { } focal)
        {
            builder.Append("&f=").Append(Format(focal.X)).Append(',').Append(Format(focal.Y));
        }

        if (spec.Crop is { } crop)
        {
            builder.Append("&c=")
                .Append(Format(crop.X)).Append(',')
                .Append(Format(crop.Y)).Append(',')
                .Append(Format(crop.Width)).Append(',')
                .Append(Format(crop.Height));
        }

        if (issuedOn is { } issued)
        {
            builder.Append('&').Append(IssuedParameter).Append('=').Append(issued.ToUnixTimeSeconds());
        }

        builder.Append('&').Append(SignatureParameter).Append('=').Append(Sign(spec, issuedOn));

        return builder.ToString();
    }

    /// <inheritdoc />
    public string SignOriginal(int mediaItemId, int editsVersion) =>
        ComputeOriginal(_key, mediaItemId, editsVersion);

    /// <inheritdoc />
    public bool ValidateOriginal(int mediaItemId, int editsVersion, string? signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        if (FixedTimeEquals(ComputeOriginal(_key, mediaItemId, editsVersion), signature)) return true;

        if (_previousKey is null) return false;

        if (_options.PreviousKeyExpiresOn is { } expiry && _clock.GetUtcNow() > expiry) return false;

        return FixedTimeEquals(ComputeOriginal(_previousKey, mediaItemId, editsVersion), signature);
    }

    /// <inheritdoc />
    public string BuildOriginalUrl(int mediaItemId, int editsVersion, string name) =>
        $"{PathPrefix}/{mediaItemId}/file/{Uri.EscapeDataString(Slug(name))}" +
        $"?v={editsVersion}&{SignatureParameter}={SignOriginal(mediaItemId, editsVersion)}";

    /// <summary>
    /// Computes the HMAC for a stored original.
    /// </summary>
    /// <param name="key">The key to sign with.</param>
    /// <param name="mediaItemId">The item.</param>
    /// <param name="editsVersion">Its edits generation.</param>
    /// <returns>The base64url signature.</returns>
    /// <remarks>
    /// A distinct payload prefix from a rendition's, so a signature issued for one can never be
    /// replayed as the other — the two are validated by different handlers with different rules
    /// about what may be served.
    /// </remarks>
    private static string ComputeOriginal(byte[] key, int mediaItemId, int editsVersion) =>
        Base64Url.EncodeToString(
            HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"original|{mediaItemId}|v{editsVersion}")));

    /// <summary>Computes the HMAC over the canonical spec.</summary>
    /// <param name="key">The key to sign with.</param>
    /// <param name="spec">The spec being signed.</param>
    /// <param name="issuedOn">The issue time, included only when one is being signed.</param>
    /// <returns>The base64url signature.</returns>
    /// <remarks>
    /// Over <see cref="RenditionSpec.ToCanonicalString"/> rather than over the URL text. Signing the
    /// URL would make the signature depend on parameter order, escaping, and the display name — none
    /// of which change the bytes produced — so a proxy that reordered a query string would break
    /// every image.
    /// </remarks>
    private static string Compute(byte[] key, RenditionSpec spec, DateTimeOffset? issuedOn)
    {
        var payload = issuedOn is { } issued
            ? $"{spec.ToCanonicalString()}|{issued.ToUnixTimeSeconds()}"
            : spec.ToCanonicalString();

        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));

        return Base64Url.EncodeToString(signature);
    }

    /// <summary>Compares two signatures without leaking where they diverge.</summary>
    /// <param name="expected">The signature computed here.</param>
    /// <param name="presented">The signature from the request.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    /// <remarks>
    /// An ordinary string comparison returns as soon as two characters differ, and the timing of
    /// that is measurable across enough requests — which is how an attacker recovers a valid
    /// signature one character at a time.
    /// </remarks>
    private static bool FixedTimeEquals(string expected, string presented) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(presented));

    private static bool TryDecodeKey(string? configured, out byte[] key)
    {
        key = [];

        if (string.IsNullOrWhiteSpace(configured)) return false;

        try
        {
            var decoded = Convert.FromBase64String(configured);

            if (decoded.Length < MediaSigningOptions.MinimumKeyBytes) return false;

            key = decoded;

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Format(double value) =>
        Math.Round(value, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Reduces a display name to something safe to put in a path segment.
    /// </summary>
    /// <param name="name">The name to use.</param>
    /// <returns>A lower-case, hyphenated name.</returns>
    /// <remarks>
    /// Cosmetic — the name is not part of the signature and not used to address anything — but it is
    /// still uploader-influenced text going into a URL, so it is reduced to a known character set
    /// rather than escaped and hoped for.
    /// </remarks>
    private static string Slug(string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);

        if (string.IsNullOrWhiteSpace(stem)) return "image";

        var builder = new StringBuilder(stem.Length);

        foreach (var character in stem.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(character)) builder.Append(character);
            else if (builder.Length > 0 && builder[^1] is not '-') builder.Append('-');
        }

        var slug = builder.ToString().Trim('-');

        return slug.Length is 0 ? "image" : slug[..Math.Min(slug.Length, 60)];
    }
}
