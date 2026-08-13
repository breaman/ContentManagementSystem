using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

using Markdig;

namespace ContentManagementSystem.Core.Content.Markdown;

/// <summary>
/// The markdown pipeline: Markdig converts, then the sanitizer cleans (task P1-19, spec section 14.4).
/// </summary>
/// <remarks>
/// Markdig is a converter, not a sanitizer, and it says so itself. CommonMark passes raw HTML through
/// untouched by design, so the HTML this produces is untrusted markup regardless of who typed the
/// source — which is why the sanitization step here has no bypass and no "trusted" overload.
/// <para>
/// <strong>This is also the only sanitization markdown ever gets.</strong> The <c>richText</c> field
/// type stores markdown exactly as authored rather than cleaning it on write (task P1-10): the raw
/// HTML markdown permits cannot be cleaned without parsing the markdown around it, and rewriting an
/// author's source to whatever a Markdig round trip produces would lose their formatting on every
/// save. So the conversion output must go through the allowlist on <em>every</em> path — preview
/// included, with no shortcut for it.
/// </para>
/// <para>
/// One pipeline, one method, one sanitizer, shared by the editor preview and by delivery. That is
/// what makes acceptance criterion P1 #7 — preview output byte-identical to delivery output —
/// structural rather than something to keep verifying.
/// </para>
/// </remarks>
public sealed class MarkdownRenderer : IMarkdownRenderer
{
    /// <summary>
    /// The pipeline. Built once, immutable, and documented by Markdig as safe to share.
    /// </summary>
    /// <remarks>
    /// Kept close to CommonMark. Two extensions are on:
    /// <list type="bullet">
    /// <item><description>
    /// <c>PipeTables</c>, because a table is the one construct authors reach for that CommonMark
    /// cannot express and the <c>Extended</c> profile can carry.
    /// </description></item>
    /// <item><description>
    /// <c>AutoLinks</c>, because a bare URL rendering as plain text reads as a bug to everyone who
    /// writes one.
    /// </description></item>
    /// </list>
    /// The rest of Markdig's advanced set is deliberately off. Each one emits markup — <c>del</c>,
    /// <c>mark</c>, <c>sub</c>, <c>sup</c>, <c>abbr</c>, footnote containers, generic attributes —
    /// that no profile in spec section 20.2 allows, so enabling it would mean an author's syntax
    /// works in the preview's source and vanishes in the sanitizer. An extension and the allowlist
    /// that has to carry it are one decision, not two.
    /// </remarks>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoLinks()
        .Build();

    private readonly IContentSanitizer _sanitizer;

    /// <summary>Creates the renderer.</summary>
    /// <param name="sanitizer">Applies the allowlist profile to the converted HTML.</param>
    public MarkdownRenderer(IContentSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        _sanitizer = sanitizer;
    }

    /// <inheritdoc />
    public string ToHtml(string? markdown, SanitizationProfile profile) =>
        string.IsNullOrEmpty(markdown)
            ? string.Empty
            : _sanitizer.Sanitize(Convert(markdown), profile);

    /// <inheritdoc />
    public SanitizationResult ToHtmlWithReport(string? markdown, SanitizationProfile profile) =>
        string.IsNullOrEmpty(markdown)
            ? SanitizationResult.Unchanged(string.Empty)
            : _sanitizer.SanitizeWithReport(Convert(markdown), profile);

    /// <summary>Converts markdown to unsanitized HTML.</summary>
    /// <param name="markdown">The authored source.</param>
    /// <remarks>
    /// Private, and returning a string nothing outside this class ever sees. The conversion is the
    /// half of the pipeline that produces hostile markup rather than removing it, and exposing it
    /// would make "render markdown without sanitizing" an available call.
    /// </remarks>
    private static string Convert(string markdown) => Markdig.Markdown.ToHtml(markdown, Pipeline);
}
