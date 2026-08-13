namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// Removes anything unsafe from author-supplied HTML, under a named allowlist profile.
/// </summary>
/// <remarks>
/// The contract lives in <c>Shared</c> rather than beside its implementation because three layers
/// call it and they do not reference each other: field types sanitize on write, renderers sanitize
/// again on read (ADR 0008), and the editor's preview has to run the identical pipeline or the
/// preview stops predicting what publishes.
/// <para>
/// Implementations must be thread-safe: this is a singleton called from every save, publish, and
/// render.
/// </para>
/// <para>
/// There is deliberately no permissive default registration. A deployment with no sanitizer wired up
/// must fail to start rather than quietly persist hostile markup.
/// </para>
/// </remarks>
public interface IContentSanitizer
{
    /// <summary>
    /// Returns the markup with everything outside the profile's allowlist removed.
    /// </summary>
    /// <param name="html">Author-supplied markup. Null or empty is returned unchanged.</param>
    /// <param name="profile">Which allowlist to apply.</param>
    /// <returns>
    /// Markup safe to store and to emit. A caller that wants to avoid rewriting an unchanged value
    /// compares the result to the input with an ordinal string comparison — a DOM-based sanitizer
    /// re-serializes even when it removed nothing, so the returned instance is not a reliable
    /// signal.
    /// </returns>
    string Sanitize(string? html, SanitizationProfile profile);
}
