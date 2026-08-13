namespace ContentManagementSystem.Core.Security;

/// <summary>
/// The parts of the sanitization policy a deployment gets to choose (spec section 20.2).
/// </summary>
/// <remarks>
/// Deliberately small. The tag, attribute, and CSS allowlists are <em>not</em> here: they are the
/// profiles, and a profile a deployment can widen is not a profile, it is a suggestion. What is
/// configurable is the three things that genuinely differ between sites — which hosts may be framed,
/// which CSS classes a designer has made available, and how large an inline image may be.
/// </remarks>
public sealed class SanitizationOptions
{
    /// <summary>Default cap on the decoded size of a <c>data:</c> image URI.</summary>
    public const int DefaultMaxDataUriBytes = 256 * 1024;

    /// <summary>
    /// Hosts an <c>iframe</c> may point at under the <c>Developer</c> profile.
    /// </summary>
    /// <remarks>
    /// Matched against the URL's host, case-insensitively and in full — <c>evil-youtube.com</c> and
    /// <c>www.youtube.com.evil.test</c> both fail, which a suffix match would not catch. An
    /// <c>iframe</c> whose <c>src</c> does not survive this is removed rather than left framing
    /// nothing.
    /// <para>
    /// The defaults are the two embed providers spec section 20.5 names. Note that section's wider
    /// point: the intended route for editor-supplied embeds is a dedicated <c>embed</c> block type,
    /// not hand-written iframe markup, and this list exists so that the <c>html</c> field type does
    /// not become the way around it.
    /// </para>
    /// </remarks>
    public ISet<string> AllowedIframeHosts { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "www.youtube.com",
        "youtube.com",
        "www.youtube-nocookie.com",
        "player.vimeo.com",
    };

    /// <summary>
    /// CSS classes authored markup may carry under the <c>Extended</c> and <c>Developer</c> profiles.
    /// </summary>
    /// <remarks>
    /// Empty means the <c>class</c> attribute is not allowed at all, which is the safe reading of
    /// "a class allowlist" — the alternative, treating an empty list as "anything goes", turns a
    /// deployment that forgot to configure this into one where an author can hang any of the site's
    /// own styles, including its overlays and its admin affordances, off arbitrary content.
    /// </remarks>
    public ISet<string> AllowedCssClasses { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Largest decoded size, in bytes, of a <c>data:</c> image URI that survives sanitization.
    /// </summary>
    /// <remarks>
    /// Inline images bloat every copy of the payload — the draft, each version row, the search
    /// document, the diff. The cap is what stops one pasted screenshot from doing that; anything
    /// bigger belongs in the media library.
    /// </remarks>
    public int MaxDataUriBytes { get; set; } = DefaultMaxDataUriBytes;
}
