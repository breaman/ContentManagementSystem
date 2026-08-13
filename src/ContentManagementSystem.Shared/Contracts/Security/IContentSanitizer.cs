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

    /// <summary>
    /// Sanitizes the markup and also reports everything the profile took out of it.
    /// </summary>
    /// <param name="html">Author-supplied markup. Null or empty reports nothing.</param>
    /// <param name="profile">Which allowlist to apply.</param>
    /// <returns>The same markup <see cref="Sanitize"/> would return, plus the account of removals.</returns>
    /// <remarks>
    /// Two callers need the account rather than only the result. The HTML editor warns an author
    /// what the active profile <em>will</em> strip before they save it, because silent stripping is
    /// the most common "the CMS ate my content" support ticket (spec section 14.4). And the XSS
    /// corpus suite asserts on what was stripped, because over-stripping is the other failure mode
    /// and a service that only returns clean markup cannot be checked for it (risk R3).
    /// <para>
    /// Collecting the account costs allocations that <see cref="Sanitize"/> does not pay, so the
    /// save and render paths keep using that one. This is for the paths where a human is waiting to
    /// read the answer.
    /// </para>
    /// </remarks>
    SanitizationResult SanitizeWithReport(string? html, SanitizationProfile profile);
}
