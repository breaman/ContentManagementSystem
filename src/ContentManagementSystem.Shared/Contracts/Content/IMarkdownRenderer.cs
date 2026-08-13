using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Converts authored markdown to the HTML a page shows, sanitized under a profile.
/// </summary>
/// <remarks>
/// There is exactly one implementation and both the editor's preview and public delivery call it,
/// which is the whole point: a preview rendered by a second pipeline is a preview that lies. Task
/// P1-19 and acceptance criterion P1 #7 state that as a requirement — the preview path and the
/// delivery path must produce byte-identical output for the same source.
/// <para>
/// The contract sits in <c>Shared</c> for the same reason <see cref="IContentSanitizer"/> does. The
/// backoffice runs in WebAssembly and cannot reference <c>Core</c>, so it reaches the one pipeline
/// through an API rather than by carrying a second copy of Markdig into the browser.
/// </para>
/// <para>
/// Implementations must be thread-safe; this is a singleton on the render path.
/// </para>
/// </remarks>
public interface IMarkdownRenderer
{
    /// <summary>
    /// Renders markdown to sanitized HTML.
    /// </summary>
    /// <param name="markdown">The authored source. Null or empty renders to an empty string.</param>
    /// <param name="profile">Which allowlist the converted HTML is sanitized under.</param>
    /// <returns>HTML that is safe to emit.</returns>
    /// <remarks>
    /// The sanitization pass is not optional and has no bypass. Markdown carries raw HTML through by
    /// design, so the converter's output is untrusted markup even when the source came from a
    /// trusted editor — and <c>richText</c> stores markdown exactly as authored rather than
    /// sanitizing it on write, which leaves this the only thing standing between stored source and
    /// a browser.
    /// </remarks>
    string ToHtml(string? markdown, SanitizationProfile profile);

    /// <summary>
    /// Renders markdown to sanitized HTML and reports what sanitization removed.
    /// </summary>
    /// <param name="markdown">The authored source.</param>
    /// <param name="profile">Which allowlist the converted HTML is sanitized under.</param>
    /// <returns>The same HTML <see cref="ToHtml"/> would return, plus the account of removals.</returns>
    /// <remarks>
    /// The editor's preview uses this so an author can see that the raw <c>&lt;iframe&gt;</c> they
    /// pasted into a markdown zone will not survive the profile — before they publish, rather than
    /// after.
    /// </remarks>
    SanitizationResult ToHtmlWithReport(string? markdown, SanitizationProfile profile);
}
