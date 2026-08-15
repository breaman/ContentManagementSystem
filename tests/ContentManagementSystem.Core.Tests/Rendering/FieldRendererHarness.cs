using Bunit;

using ContentManagementSystem.Core.Content.Markdown;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Core.Tests.Content;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// A bUnit context wired the way a deployment is, for the field renderer tests (task P3-09).
/// </summary>
/// <remarks>
/// The renderer catalog here is the <em>real</em> one, built over the real field type registry, so
/// these tests exercise the mapping a site runs on rather than a table restated in the test project.
/// The services the renderers reach for are real too where the real one is cheap and deterministic —
/// the sanitizer and the markdown pipeline both are, and substituting them would mean the tests
/// asserting that markup is safe were asserting it about a double.
/// <para>
/// What is faked is only what needs a database: link resolution and the schema catalog.
/// </para>
/// </remarks>
internal sealed class FieldRendererHarness : IDisposable
{
    /// <summary>
    /// The field type registry a real deployment builds, from the real assembly scan.
    /// </summary>
    /// <remarks>
    /// Not <c>ContentEngineHarness.Registry</c>, which is assembled by hand and deliberately omits
    /// several field types so the payload-engine tests can reach their unknown-key cases. A renderer
    /// catalog built over a hand-picked registry would silently have no entry for <c>date</c>,
    /// <c>color</c>, or <c>json</c> — and this task's whole point is that every registered field type
    /// resolves to a renderer.
    /// </remarks>
    public static IFieldTypeRegistry Registry { get; } = new ServiceCollection()
        .AddCmsSanitization()
        .AddCmsFieldTypes()
        .BuildServiceProvider()
        .GetRequiredService<IFieldTypeRegistry>();

    private readonly BunitContext _bunit = new();

    public FieldRendererHarness()
    {
        var sanitizer = new SanitizationService();

        _bunit.Services.AddLogging(logging => logging.AddProvider(Logs));
        _bunit.Services.AddSingleton<IContentSanitizer>(sanitizer);
        _bunit.Services.AddSingleton<IMarkdownRenderer>(new MarkdownRenderer(sanitizer));
        _bunit.Services.AddSingleton<IFieldRendererCatalog>(new FieldRendererCatalog(Registry));
        _bunit.Services.AddSingleton<ILinkResolver>(Links);
        _bunit.Services.AddSingleton<IContentSchemaCatalog>(provider => Schemas);
        _bunit.Services.AddSingleton<ICmsComponentCatalog>(new CmsComponentCatalog(
            CmsComponentScanner.ScanTypes([typeof(TestTemplate), typeof(TestBlock), typeof(QuoteBlock)])));
    }

    /// <summary>Log entries, so a test can assert a fallback was reported and not only taken.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>The page ids this render can resolve, and what they resolve to.</summary>
    public TestLinkResolver Links { get; } = new();

    /// <summary>The block type revisions this render can resolve configuration from.</summary>
    public IContentSchemaCatalog Schemas { get; set; } = ContentSchemaCatalog.Empty;

    public void Dispose()
    {
        _bunit.Dispose();
        Logs.Dispose();
    }

    /// <summary>Renders one zone of a payload, which is how every field renderer is reached.</summary>
    /// <param name="zoneJson">The zone's stored value, written as JSON.</param>
    /// <param name="schema">The captured template schema, when the test supplies one.</param>
    /// <param name="mode">Which audience the render is for.</param>
    /// <param name="zoneKey">The zone key.</param>
    public string Render(
        string zoneJson,
        ContentSchema? schema = null,
        CmsRenderMode mode = CmsRenderMode.Live,
        string zoneKey = "hero") =>
        RenderIn(
            RenderingHarness.Context(RenderingHarness.Payload((zoneKey, zoneJson)), schema, mode),
            zoneKey);

    /// <summary>
    /// Renders a zone of a context the caller built, so it can read the cache tags afterwards.
    /// </summary>
    /// <param name="context">The render context.</param>
    /// <param name="zoneKey">The zone key.</param>
    /// <remarks>
    /// Tags accumulate <em>during</em> the render, which is why a test that asserts on them has to
    /// hold the context rather than take one back from the markup.
    /// </remarks>
    public string RenderIn(RenderContext context, string zoneKey) =>
        _bunit.Render<CmsZone>(parameters => parameters
                .Add(zone => zone.Name, zoneKey)
                .AddCascadingValue(context))
            .Markup;

    /// <summary>Builds a template schema declaring one zone, for the configuration cases.</summary>
    /// <param name="fieldTypeKey">Key of the field type filling the zone.</param>
    /// <param name="configurationJson">The captured configuration.</param>
    /// <param name="zoneKey">The zone key.</param>
    public static ContentSchema Schema(
        string fieldTypeKey,
        string? configurationJson = null,
        string zoneKey = "hero") =>
        ContentEngineHarness.Template(
            RenderingHarness.TemplateKey,
            1,
            ContentPropertySchema.Create(zoneKey, zoneKey, fieldTypeKey, configurationJson));
}

/// <summary>
/// A link resolver a test states the contents of outright.
/// </summary>
/// <remarks>
/// It honours <c>includeUnpublished</c> rather than ignoring it, because that flag is the whole
/// difference between preview and the public site (spec section 12.3) and a double that returned
/// everything regardless would make the leak the flag prevents untestable.
/// </remarks>
internal sealed class TestLinkResolver : ILinkResolver
{
    private readonly Dictionary<int, ResolvedLink> _pages = [];

    /// <summary>Adds a page this resolver can find.</summary>
    /// <param name="pageId">The page id.</param>
    /// <param name="url">Its URL.</param>
    /// <param name="title">Its current title.</param>
    /// <param name="isPublished">Whether it is live.</param>
    public TestLinkResolver Add(int pageId, string url, string title = "About us", bool isPublished = true)
    {
        _pages[pageId] = new ResolvedLink(pageId, url, isPublished, title);

        return this;
    }

    public Task<IReadOnlyDictionary<int, ResolvedLink>> ResolveAsync(
        IEnumerable<int> pageIds,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        var resolved = new Dictionary<int, ResolvedLink>();

        foreach (var pageId in pageIds.Distinct())
        {
            if (!_pages.TryGetValue(pageId, out var page)) continue;

            resolved[pageId] = page.IsPublished || includeUnpublished
                ? page
                : page with { Url = null };
        }

        return Task.FromResult<IReadOnlyDictionary<int, ResolvedLink>>(resolved);
    }
}

/// <summary>A block component reading a structured property through <see cref="CmsBlockProperty"/>.</summary>
/// <remarks>
/// <see cref="TestBlock"/> covers the text-shaped path; this one covers the path that matters more,
/// where a block's property is rendered by the field type's own renderer rather than read out by
/// hand.
/// </remarks>
[CmsBlockType("quote", "Pull Quote")]
internal sealed class QuoteBlock : CmsBlockBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "figure");
        builder.AddAttribute(1, "data-block", BlockId.ToString());
        builder.AddAttribute(2, "data-revision", BlockTypeRevision.ToString());
        builder.OpenComponent<CmsBlockProperty>(3);
        builder.AddComponentParameter(4, nameof(CmsBlockProperty.Name), "quote");
        builder.CloseComponent();
        builder.CloseElement();
    }
}
