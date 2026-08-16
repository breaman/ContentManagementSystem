using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// Renders authored source the way the published page will render it (task P6-09).
/// </summary>
/// <remarks>
/// Implemented twice, following this project's pre-rendering pattern: over <c>HttpClient</c> in the
/// WebAssembly client, and directly over <c>IMarkdownRenderer</c> and <c>IContentSanitizer</c> on
/// the server. The second implementation is not only for pre-render — it is what makes the seam
/// honest, because it demonstrates that the browser is reaching the same two singletons the delivery
/// path calls rather than an endpoint that happens to look similar.
/// <para>
/// Failure returns null rather than throwing. A preview that cannot be fetched is a pane that says
/// so; it is not a reason to take the editor down around content somebody has not saved yet.
/// </para>
/// </remarks>
public interface IMarkupPreviewClient
{
    /// <summary>Renders source to the markup the page will show.</summary>
    /// <param name="request">What to render, and under which allowlist.</param>
    /// <param name="cancellationToken">Token observed while rendering.</param>
    /// <returns>The rendered markup and what was removed, or null when the request failed.</returns>
    Task<MarkupPreviewResult?> RenderAsync(
        MarkupPreviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Lists what each sanitization profile keeps, for the permitted-tags banner (P6-13).</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>One descriptor per profile, empty when the request failed.</returns>
    Task<IReadOnlyList<SanitizationProfileDescriptor>> GetProfilesAsync(
        CancellationToken cancellationToken = default);
}
