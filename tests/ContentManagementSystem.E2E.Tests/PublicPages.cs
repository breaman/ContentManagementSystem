using System.Text.Json;

using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Navigation;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Server.Delivery;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Markdown;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Renders the public document a visitor receives, for the gates that judge one (tasks P9-07, P9-09).
/// </summary>
/// <remarks>
/// <c>CmsDeliveryDocument</c> itself, not a re-creation of it. A gate that judged a look-alike would
/// go green on a shell nobody serves, and the shell is where most of what an accessibility audit
/// looks at lives: the <c>lang</c> attribute, the landmarks, the navigation, the heading that opens
/// the page.
/// <para>
/// Everything the render reaches for is the real implementation except the navigation reader, which
/// is a database query and is faked with the shape of a small site's menu. That is the honest line:
/// what the menu <em>contains</em> is content, and what the component makes of it is what is being
/// judged.
/// </para>
/// </remarks>
internal static class PublicPages
{
    /// <summary>The template key the fixture publishes against.</summary>
    public const string TemplateKey = "article";

    /// <summary>
    /// Renders one public page to a complete HTML document.
    /// </summary>
    /// <param name="title">The page title, which becomes the document's <c>h1</c> and its title.</param>
    /// <param name="zones">The zone payloads, as they are stored.</param>
    /// <param name="customStylesheetHref">
    /// Where the administrator-authored stylesheet lives, or null when nothing is published — which
    /// is what a deployment that has never used the feature renders (task P10-07).
    /// </param>
    /// <returns>The document.</returns>
    public static async Task<string> RenderAsync(
        string title,
        string zones,
        string? customStylesheetHref = null)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));

        // The real render path: the component catalog scanned out of the rendering assembly, the
        // field renderer catalog built from the registered field types, and the sanitizer the
        // render-time pass of ADR-0008 goes through.
        services.AddCmsSanitization();
        services.AddCmsFieldTypes();
        // The rich-text renderer's markdown pipeline. Registered directly rather than through
        // AddCmsContent, whose schema catalog reads a database this gate has no use for.
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        services.AddCmsComponentScanning(typeof(RenderingAssemblyMarker).Assembly);
        services.AddCmsRendering();

        services.AddSingleton<INavigationService, FakeNavigationService>();

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new PrerenderingHtmlRenderer(
            provider,
            provider.GetRequiredService<ILoggerFactory>());

        var content = Content(title, zones);

        return await renderer.RenderAsync(
            typeof(CmsDeliveryDocument),
            ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(CmsDeliveryDocument.Context)] = RenderContext.For(content, CmsRenderMode.Live),
                [nameof(CmsDeliveryDocument.Seo)] = Seo(title),
                [nameof(CmsDeliveryDocument.CustomStylesheetHref)] = customStylesheetHref,
            }));
    }

    /// <summary>One published version, assembled by hand.</summary>
    /// <param name="title">The page title.</param>
    /// <param name="zones">The zone payloads.</param>
    /// <returns>The content.</returns>
    /// <remarks>
    /// The schema is null, which is a supported rendering condition rather than a shortcut: the
    /// payload's own type discriminators are what the render path reads, and only field
    /// configuration is lost (spec section 8.5). It is also the state a page is in when its template
    /// revision has been superseded, so rendering without one is worth exercising.
    /// </remarks>
    private static PublishedContent Content(string title, string zones) =>
        new(
            PageId: 12,
            PublicId: Guid.Parse("2f3c8f2e-6a5f-4a5f-9c3a-0f7c1a2b3c4d"),
            VersionId: 34,
            VersionNumber: 3,
            Title: title,
            Url: "/pricing",
            TemplateId: 1,
            TemplateKey: TemplateKey,
            TemplateRevision: 1,
            IsPublished: true,
            PublishedOn: DateTimeOffset.Parse("2026-05-04T09:00:00Z", null),
            ModifiedOn: DateTimeOffset.Parse("2026-05-04T09:00:00Z", null),
            Seo: PublishedSeo.Default,
            Payload: ContentPayload.Parse($$"""
                { "schemaVersion": 1, "templateKey": "{{TemplateKey}}", "templateRevision": 1,
                  "zones": { {{zones}} } }
                """),
            Schema: null);

    /// <summary>The document head, as the builder would have resolved it.</summary>
    /// <param name="title">The page title.</param>
    /// <returns>The metadata.</returns>
    private static SeoMetadata Seo(string title) =>
        new(
            Title: title,
            Description: "What each plan costs and what it includes.",
            CanonicalUrl: "https://example.test/pricing",
            Robots: SeoMetadata.IndexFollow,
            Meta:
            [
                SeoMetaTag.Property("og:title", title),
                SeoMetaTag.Named("twitter:card", "summary"),
            ],
            JsonLd: [],
            OgImageMediaId: null,
            Language: "en-GB");

    /// <summary>A zone payload holding rich text.</summary>
    /// <param name="key">The zone key.</param>
    /// <param name="markup">The stored markup.</param>
    /// <returns>The JSON member.</returns>
    public static string RichText(string key, string markup) =>
        $$"""
          "{{key}}": { "type": "richText", "format": "html", "value": {{JsonSerializer.Serialize(markup)}} }
          """;

    /// <summary>
    /// A zone payload holding raw HTML.
    /// </summary>
    /// <param name="key">The zone key.</param>
    /// <param name="markup">The stored markup.</param>
    /// <returns>The JSON member.</returns>
    /// <remarks>
    /// The widest sanitization profile, and the only one that keeps a table. A table cannot be
    /// authored in the WYSIWYG toolbar at all — it arrives through the HTML source editor or a paste
    /// — so this is the field type one is actually stored in, and rendering reads the field type from
    /// the payload's own discriminator rather than from the zone's declaration.
    /// </remarks>
    public static string Html(string key, string markup) =>
        $$"""
          "{{key}}": { "type": "html", "value": {{JsonSerializer.Serialize(markup)}} }
          """;

    /// <summary>A zone payload holding plain text.</summary>
    /// <param name="key">The zone key.</param>
    /// <param name="text">The stored text.</param>
    /// <returns>The JSON member.</returns>
    public static string PlainText(string key, string text) =>
        $$"""
          "{{key}}": { "type": "plainText", "value": {{JsonSerializer.Serialize(text)}} }
          """;

    /// <summary>
    /// A small site's menu, standing in for the query that reads one.
    /// </summary>
    /// <remarks>
    /// Two levels, because a nested list is where a navigation component's markup usually goes wrong
    /// and a flat one would prove nothing about it.
    /// </remarks>
    private sealed class FakeNavigationService : INavigationService
    {
        public Task<IReadOnlyList<NavigationNode>> GetStructuralAsync(
            int maxDepth = 2,
            int? rootPageId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NavigationNode>>(
            [
                NavigationNode.Leaf("Home", "/", 1),
                new NavigationNode("Products", "/products", 2, false,
                [
                    NavigationNode.Leaf("Pricing", "/pricing", 12),
                    NavigationNode.Leaf("Enterprise", "/products/enterprise", 13),
                ]),
                NavigationNode.Leaf("About", "/about", 3),
            ]);

        public Task<IReadOnlyList<NavigationNode>> GetMenuAsync(
            string menuKey,
            CancellationToken cancellationToken = default) =>
            GetStructuralAsync(cancellationToken: cancellationToken);
    }
}
