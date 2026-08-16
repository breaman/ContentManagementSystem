namespace ContentManagementSystem.Core.Media.Delivery;

/// <summary>
/// The rendition signing key and its rotation state (tasks P5-14 and P5-18, spec section 20.8).
/// </summary>
/// <remarks>
/// <strong>Rotation needs two keys and a grace period, not one key that changes.</strong> Rendition
/// URLs are embedded in every cached page, every CDN copy, and every email that quoted one. Swapping
/// the key atomically would invalidate all of them at once — every image on the site breaking
/// simultaneously, for as long as the caches take to turn over. During the grace period the previous
/// key still validates, so old URLs keep working while newly rendered pages emit new ones.
/// </remarks>
public sealed class MediaSigningOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Cms:MediaSigning";

    /// <summary>Shortest key accepted, in bytes.</summary>
    /// <remarks>
    /// 256 bits, matching the HMAC's own output. A shorter key adds no security and a longer one is
    /// hashed down to the block size anyway, so this is both the floor and the sensible value.
    /// </remarks>
    public const int MinimumKeyBytes = 32;

    /// <summary>
    /// The key currently used to sign, base64-encoded.
    /// </summary>
    /// <remarks>
    /// A secret. It belongs in user secrets in development and in a key vault in production, never
    /// in <c>appsettings.json</c> — anyone holding it can make the server encode arbitrary
    /// renditions, which is exactly the denial of service the signature exists to prevent
    /// (spec section 20.8).
    /// </remarks>
    public string? Key { get; set; }

    /// <summary>The key being rotated out, base64-encoded. Validates but never signs.</summary>
    public string? PreviousKey { get; set; }

    /// <summary>When the previous key stops validating.</summary>
    /// <remarks>
    /// Null means it is honoured indefinitely, which is a configuration mistake rather than a
    /// feature: a rotation that never completes has not removed the old key from anything.
    /// </remarks>
    public DateTimeOffset? PreviousKeyExpiresOn { get; set; }

    /// <summary>
    /// How long a signature stays valid once issued, or null for indefinitely.
    /// </summary>
    /// <remarks>
    /// Null by default, and that is the right default here. A rendition URL is public, immutable
    /// content addressed by a hash of its own parameters; expiring it would break cached pages and
    /// CDN copies for no benefit, because the signature guards the encode surface rather than an
    /// authorization decision. Deployments that serve media only to signed-in users can set it.
    /// </remarks>
    public TimeSpan? SignatureLifetime { get; set; }
}
