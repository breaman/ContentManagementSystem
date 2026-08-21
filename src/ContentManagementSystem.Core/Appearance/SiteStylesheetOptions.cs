namespace ContentManagementSystem.Core.Appearance;

/// <summary>
/// Configuration of the administrator-authored site stylesheet (spec section 30).
/// </summary>
public sealed class SiteStylesheetOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:SiteStylesheet";

    /// <summary>Default cap on the published stylesheet, in bytes.</summary>
    public const int DefaultMaxBytes = 256 * 1024;

    /// <summary>
    /// Largest stylesheet that may be saved or published, in UTF-8 bytes.
    /// </summary>
    /// <remarks>
    /// It is a stylesheet, not a bundle. The default is generous for hand-written CSS and small
    /// enough that the response stays a rounding error against the page it styles. Configurable
    /// because "generous" is a judgement about a site nobody has seen, and refusing a legitimate
    /// stylesheet at a number chosen in advance would leave an administrator with no way forward
    /// that is not a deployment.
    /// </remarks>
    public int MaxBytes { get; set; } = DefaultMaxBytes;

    /// <summary>
    /// How long a shared cache may hold the stylesheet response before revalidating, in seconds.
    /// </summary>
    /// <remarks>
    /// Matches the page policy of spec section 16.1 for the same reason: a shared cache may hold
    /// it, a browser must ask. A publish evicts the server-side entry immediately, so this bounds
    /// only what a CDN in front of the site may serve without asking (Q6).
    /// </remarks>
    public int SharedMaxAgeSeconds { get; set; } = 300;
}
