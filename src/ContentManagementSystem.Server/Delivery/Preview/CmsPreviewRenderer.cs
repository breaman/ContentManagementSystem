using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// Renders the preview chrome document to a string (task P3-16).
/// </summary>
/// <param name="services">
/// The request's service provider. Scoped for the reason <c>CmsPageRenderer</c> gives: a root
/// provider would hand every request the same database context.
/// </param>
/// <param name="loggerFactory">Passed to the component renderer, which requires one.</param>
/// <remarks>
/// Separate from <c>CmsPageRenderer</c> even though the mechanics are the same, because what they
/// produce is not: this document has no cache tags to accumulate, nothing to measure against the
/// public site's render histogram, and nothing that may ever be cached. Folding the two together
/// would mean a parameter deciding which of those applied, on the one path where getting it wrong
/// caches an unpublished page.
/// </remarks>
public sealed class CmsPreviewRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
{
    /// <summary>
    /// Renders the toolbar and frame around a page.
    /// </summary>
    /// <param name="chrome">What is on screen, and where the links go.</param>
    /// <returns>The complete outer document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chrome"/> is null.</exception>
    public async Task<string> RenderAsync(PreviewChrome chrome)
    {
        ArgumentNullException.ThrowIfNull(chrome);

        await using var renderer = new HtmlRenderer(services, loggerFactory);

        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CmsPreviewDocument.Chrome)] = chrome,
            });

            var output = await renderer.RenderComponentAsync<CmsPreviewDocument>(parameters);

            return output.ToHtmlString();
        });
    }
}
