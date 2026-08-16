using Bunit;

using ContentManagementSystem.Core.Content.Markdown;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Core.Tests.Content;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
/// What is faked is only what needs a database: link, reusable, and media resolution, and the
/// schema catalog. The media URL signer is the real one over a fixed key, so a rendered
/// <c>srcset</c> holds signatures the delivery endpoint would actually accept.
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
        _bunit.Services.AddSingleton<IReusableContentResolver>(Reusable);
        _bunit.Services.AddSingleton<IMediaResolver>(Media);

        // The real signer over a fixed key. Substituting it would make the picture tests assert
        // against URLs a double invented, and the one property worth proving about a srcset is that
        // every candidate in it is one this deployment would actually accept (task P5-20).
        _bunit.Services.AddSingleton<IMediaUrlSigner>(new MediaUrlSigner(
            new MediaSigningOptions { Key = Convert.ToBase64String(new byte[32]) },
            TimeProvider.System,
            NullLogger<MediaUrlSigner>.Instance));
        _bunit.Services.AddSingleton<IContentSchemaCatalog>(provider => Schemas);
        _bunit.Services.AddSingleton<IFieldTypeRegistry>(Registry);

        // The test components plus everything the Rendering assembly ships — the two reference
        // templates and three reference block types of task P3-10. Scanned together through the real
        // scanner, so the duplicate-key rule a deployment enforces is enforced here too: a test
        // component that claimed a reference key would fail loudly rather than shadow it.
        _bunit.Services.AddSingleton<ICmsComponentCatalog>(new CmsComponentCatalog(
            CmsComponentScanner.ScanTypes(
                [
                    typeof(TestTemplate),
                    typeof(TestBlock),
                    typeof(QuoteBlock),
                    typeof(NestableBlock),
                    .. typeof(RenderingAssemblyMarker).Assembly.GetTypes(),
                ])));
    }

    /// <summary>Log entries, so a test can assert a fallback was reported and not only taken.</summary>
    public RecordingLoggerProvider Logs { get; } = new();

    /// <summary>The page ids this render can resolve, and what they resolve to.</summary>
    public TestLinkResolver Links { get; } = new();

    /// <summary>The reusable items this render can resolve, and what they resolve to.</summary>
    public TestReusableContentResolver Reusable { get; } = new();

    /// <summary>The media items this render can resolve, and what they resolve to.</summary>
    public TestMediaResolver Media { get; } = new();

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

    /// <summary>Renders a whole page from the host down, the way delivery does.</summary>
    /// <param name="context">The render context.</param>
    /// <remarks>
    /// The difference from <see cref="RenderIn"/> is what is under test: this exercises template
    /// resolution and zone placement as well as the renderers, which is what the reference template
    /// and error boundary suites need and what a single-zone render cannot show.
    /// </remarks>
    public string RenderPage(RenderContext context) =>
        _bunit.Render<CmsPageHost>(parameters => parameters
                .Add(host => host.Context, context))
            .Markup;

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

/// <summary>
/// A media resolver a test states the contents of outright (task P5-20).
/// </summary>
/// <remarks>
/// Faked for the reason the link resolver is: it needs a database and nothing about the markup under
/// test depends on how the row was read. Everything downstream of it is real — the geometry, the
/// candidate widths, and the signature are the deployment's own, so a test that asserts on a
/// <c>srcset</c> is asserting about URLs the delivery endpoint would accept.
/// </remarks>
internal sealed class TestMediaResolver : IMediaResolver
{
    private readonly Dictionary<int, ResolvedMedia> _items = [];

    /// <summary>Adds an item this resolver can find.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="width">Pixel width of the stored original.</param>
    /// <param name="height">Pixel height of the stored original.</param>
    /// <param name="altText">The item's own alternative text.</param>
    /// <param name="isDecorative">Whether it renders <c>alt=""</c>.</param>
    /// <param name="contentType">The sniffed media type, which decides the fallback format.</param>
    /// <param name="kind">What the file is.</param>
    /// <param name="edits">The library-scope edits.</param>
    /// <param name="editsVersion">The edits generation.</param>
    /// <param name="caption">Caption rendered beneath the picture.</param>
    public TestMediaResolver Add(
        int id,
        int? width = 2000,
        int? height = 1500,
        string? altText = "A quiet street",
        bool isDecorative = false,
        string contentType = "image/jpeg",
        MediaKind kind = MediaKind.Image,
        MediaEdits? edits = null,
        int editsVersion = 0,
        string? caption = null)
    {
        _items[id] = new ResolvedMedia(
            id,
            kind,
            contentType,
            $"photo-{id}.jpg",
            width,
            height,
            altText,
            isDecorative,
            null,
            caption,
            null,
            edits ?? MediaEdits.None,
            editsVersion);

        return this;
    }

    public Task<IReadOnlyDictionary<int, ResolvedMedia>> ResolveAsync(
        IEnumerable<int> mediaIds,
        CancellationToken cancellationToken = default)
    {
        var resolved = new Dictionary<int, ResolvedMedia>();

        foreach (var id in mediaIds.Distinct())
        {
            if (_items.TryGetValue(id, out var item)) resolved[id] = item;
        }

        return Task.FromResult<IReadOnlyDictionary<int, ResolvedMedia>>(resolved);
    }
}

/// <summary>
/// A reusable-content resolver a test states the contents of outright (task P4-06).
/// </summary>
/// <remarks>
/// It enforces the guards the real one does — the pin has to name a version of the item it is
/// placed against, an unpublished item resolves only under <c>includeUnpublished</c>, and the chain
/// refuses a loop or a descent past the depth ceiling. A double that skipped them would make every
/// one of those behaviours untestable at the renderer, which is the level where the consequence
/// (half a page, or a stack overflow on a public request) actually appears.
/// </remarks>
internal sealed class TestReusableContentResolver : IReusableContentResolver
{
    private readonly Dictionary<int, Item> _items = [];

    /// <summary>Adds an item this resolver can find.</summary>
    /// <param name="id">The item id.</param>
    /// <param name="blockTypeKey">Block type shaping it, which selects the component.</param>
    /// <param name="versions">Version id and payload pairs, in the order they were published.</param>
    /// <param name="publishedVersionId">The version late-bound placements render, or null.</param>
    /// <param name="name">Display name, shown by the preview badge.</param>
    public TestReusableContentResolver Add(
        int id,
        string blockTypeKey,
        (int VersionId, ContentPayload Payload)[] versions,
        int? publishedVersionId,
        string name = "Site footer")
    {
        _items[id] = new Item(id, $"item-{id}", name, blockTypeKey, publishedVersionId, versions);

        return this;
    }

    public Task<ResolvedReusableContent> ResolveAsync(
        int reusableContentId,
        int? pinnedVersionId,
        ReusableResolutionChain chain,
        bool includeUnpublished = false,
        CancellationToken cancellationToken = default)
    {
        if (chain.Contains(reusableContentId))
        {
            return Unresolved(ReusableResolutionStatus.Cycle, reusableContentId);
        }

        if (chain.IsAtMaxDepth) return Unresolved(ReusableResolutionStatus.TooDeep, reusableContentId);

        if (!_items.TryGetValue(reusableContentId, out var item))
        {
            return Unresolved(ReusableResolutionStatus.NotFound, reusableContentId);
        }

        var versionId = pinnedVersionId ?? item.PublishedVersionId ??
            (includeUnpublished ? item.Versions[^1].VersionId : null);

        if (versionId is null) return Unresolved(ReusableResolutionStatus.NotPublished, reusableContentId);

        var version = Array.FindIndex(item.Versions, candidate => candidate.VersionId == versionId);

        if (version < 0)
        {
            return Unresolved(
                pinnedVersionId is null
                    ? ReusableResolutionStatus.NotPublished
                    : ReusableResolutionStatus.PinnedVersionMissing,
                reusableContentId);
        }

        return Task.FromResult(new ResolvedReusableContent(
            ReusableResolutionStatus.Resolved,
            item.Id,
            item.Key,
            item.Name,
            item.Versions[version].VersionId,
            version + 1,
            pinnedVersionId is not null,
            item.Versions[version].VersionId == item.PublishedVersionId,
            item.PublishedVersionId is null
                ? null
                : Array.FindIndex(item.Versions, candidate => candidate.VersionId == item.PublishedVersionId) + 1,
            item.BlockTypeKey,
            1,
            item.Versions[version].Payload,
            null));
    }

    private static Task<ResolvedReusableContent> Unresolved(ReusableResolutionStatus status, int id) =>
        Task.FromResult(ResolvedReusableContent.Unresolved(status, id));

    private sealed record Item(
        int Id,
        string Key,
        string Name,
        string BlockTypeKey,
        int? PublishedVersionId,
        (int VersionId, ContentPayload Payload)[] Versions);
}

/// <summary>
/// A block component that can hold another reusable placement inside it (task P4-06).
/// </summary>
/// <remarks>
/// Nesting is a property of the <em>block type</em>, not of the reusable machinery: a component
/// renders the properties its markup names, so an item shaped by a block type whose markup mentions
/// no <c>reusable</c> property can never contain one however the payload is authored. The built-in
/// <c>rawHtml</c> type is exactly that — one HTML property — so the depth and cycle guards need a
/// shape that does nest to be reachable at all.
/// </remarks>
[CmsBlockType("nestable", "Nestable")]
internal sealed class NestableBlock : CmsBlockBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-block", BlockId.ToString());
        builder.OpenComponent<CmsBlockProperty>(2);
        builder.AddComponentParameter(3, nameof(CmsBlockProperty.Name), "content");
        builder.CloseComponent();
        builder.OpenComponent<CmsBlockProperty>(4);
        builder.AddComponentParameter(5, nameof(CmsBlockProperty.Name), "nested");
        builder.CloseComponent();
        builder.CloseElement();
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
