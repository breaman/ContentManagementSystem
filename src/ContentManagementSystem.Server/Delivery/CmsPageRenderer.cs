using System.Diagnostics;

using ContentManagementSystem.Core.Appearance;
using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Server.Delivery.Appearance;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Server.Delivery;

/// <summary>One rendered page: the markup, and everything the render turned out to depend on.</summary>
/// <param name="Html">The complete document.</param>
/// <param name="CacheTags">
/// The tags accumulated while rendering (spec section 16.2). Complete only because the render
/// finished before this record existed — that ordering is the whole point of rendering to a string.
/// </param>
public sealed record RenderedPage(string Html, IReadOnlyList<string> CacheTags);

/// <summary>
/// Renders a loaded page version to a complete HTML document (task P3-13).
/// </summary>
/// <param name="services">
/// The request's service provider. Scoped deliberately: the renderers below resolve
/// <c>ILinkResolver</c> and <c>IContentSchemaCatalog</c>, both of which hold a database context, and
/// a root provider here would give every request the same one.
/// </param>
/// <param name="loggerFactory">Passed to the component renderer, which requires one.</param>
/// <param name="seo">Resolves the document head this page is served with (task P8-01).</param>
/// <param name="stylesheet">
/// Answers whether there is an administrator-authored stylesheet to link (task P10-07). Asked for
/// the entity tag rather than the CSS, because the document needs to know only whether one exists
/// and reading an <c>nvarchar(max)</c> column to decide that would put the whole stylesheet on the
/// wire once per render.
/// </param>
/// <param name="metrics">Records how long the render took, tagged by template (task P3-28).</param>
/// <remarks>
/// <strong>Render to a string, then set headers, then write.</strong> Cache tags accumulate
/// <em>during</em> the render, so a response that sent its headers first would carry an incomplete
/// set and produce a page that never invalidates — a bug with no symptom until content changes and
/// the old page keeps being served. The S2 spike found that <c>RazorComponentResult</c> happens to
/// work today only because the response is buffered, and that one <c>[StreamRendering]</c> attribute
/// anywhere below would silently break it. <see cref="HtmlRenderer"/> cannot regress that way, which
/// is why it is used here.
/// <para>
/// A renderer per render, not one shared. <see cref="HtmlRenderer"/> owns a dispatcher and a render
/// tree, is not safe to use from two requests at once, and is cheap enough that the S2 spike measured
/// a fresh one per render as part of its ~7 µs-per-block figure.
/// </para>
/// </remarks>
public sealed class CmsPageRenderer(
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    ISeoMetadataBuilder seo,
    IPublishedStylesheetReader stylesheet,
    CmsMetrics metrics)
{
    /// <summary>
    /// Renders one version to a complete document.
    /// </summary>
    /// <param name="content">The loaded version.</param>
    /// <param name="mode">Which audience the render is for.</param>
    /// <param name="cancellationToken">Token observed while the head's metadata is queried.</param>
    /// <returns>The document and the cache tags it depends on.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <remarks>
    /// The token reaches the head's queries and stops there: the render itself is not cancellable,
    /// and that is not an oversight. The document is built into a buffer that has not been written
    /// anywhere, so abandoning it half-way would leave the caller with a partial page and nothing
    /// useful to do about it, and the work being abandoned is measured in microseconds.
    /// </remarks>
    public async Task<RenderedPage> RenderAsync(
        PublishedContent content,
        CmsRenderMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var context = RenderContext.For(content, mode);

        // Resolved before the render rather than during it. The head reads site settings, the
        // ancestor trail, and the share image's library record, and a component that queried while
        // rendering would make what the head says depend on when it happened to run.
        var metadata = await seo.BuildAsync(content, context.IsPreview, cancellationToken);

        // Which stylesheet, decided here rather than in the document: a preview frame shows the
        // draft so a redesign can be judged on real pages, and a live render shows only what has
        // been published (spec section 30.4). A preview always links it — the draft may be empty,
        // and an empty preview stylesheet is a cheap 200 rather than a branch that could get the
        // two modes the wrong way round.
        var customStylesheet = context.IsPreview
            ? SiteStylesheetEndpoint.PreviewHref
            : await stylesheet.GetPublishedETagAsync(cancellationToken) is null
                ? null
                : SiteStylesheetEndpoint.Path;

        var started = Stopwatch.GetTimestamp();

        await using var renderer = new HtmlRenderer(services, loggerFactory);

        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CmsDeliveryDocument.Context)] = context,
                [nameof(CmsDeliveryDocument.Seo)] = metadata,
                [nameof(CmsDeliveryDocument.CustomStylesheetHref)] = customStylesheet,
            });

            var output = await renderer.RenderComponentAsync<CmsDeliveryDocument>(parameters);

            // RenderComponentAsync waits for quiescence, so every asynchronous renderer below has
            // finished by this line — which is exactly what makes the tag set complete.
            return output.ToHtmlString();
        });

        // Only the public render is measured. `cms.page.render.duration` answers "what does the
        // public site cost", and preview traffic is a handful of editors rendering deliberately
        // unusual versions — folding it in would move the percentiles of a series that is watched
        // for regressions, in a direction nobody could attribute (spec section 24.1).
        if (mode is CmsRenderMode.Live)
        {
            metrics.RecordPageRender(content.TemplateKey, Stopwatch.GetElapsedTime(started));
        }

        return new RenderedPage(html, context.CacheTags.ToArray());
    }
}
