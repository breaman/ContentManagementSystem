using System.Text.Json;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>media</c> value — one picked image or file (tasks P5-19 and P5-20,
/// spec sections 7.1 and 13.6).
/// </summary>
/// <remarks>
/// The payload holds an id, a placement's alternative text, and its geometry; everything else about
/// the picture is resolved here, at render time, from the library. That is what makes replacing an
/// image or fixing its alt text change every page showing it without any of them being republished
/// (spec section 13.1) — and it is the same late-binding argument decision D6 makes for internal
/// links.
/// <para>
/// <strong>The cache tag is declared whether or not the item resolves.</strong> A page that renders
/// media item 812 depends on item 812 even when 812 is in the recycle bin: restoring it has to evict
/// this page, and a tag added only on success would never fire (spec section 16.2).
/// </para>
/// <para>
/// Four outcomes, all ordinary (spec section 15.3): a responsive <c>&lt;picture&gt;</c> for an image
/// with renditions; a plain <c>&lt;img&gt;</c> at the signed original for an SVG or GIF, which have
/// none; a download link for a document; and the placeholder carrying the alternative text for an
/// item nothing can resolve. None of them throws, because a withdrawn image must not take a page
/// down.
/// </para>
/// </remarks>
public partial class MediaRenderer : CmsFieldRendererBase
{
    /// <summary>The identity of the picked item — the member that decides whether anything is picked.</summary>
    private const string MediaIdMember = "mediaId";

    /// <summary>Alternative text overriding the item's own, for this placement only.</summary>
    private const string AltOverrideMember = "altOverride";

    /// <summary>The placement's focal point.</summary>
    private const string FocalPointMember = "focalPoint";

    /// <summary>The placement's crop.</summary>
    private const string CropMember = "crop";

    /// <summary>The configured <c>sizes</c> attribute for this slot.</summary>
    private const string SizesSetting = "sizes";

    [Inject]
    private IMediaResolver Media { get; set; } = default!;

    [Inject]
    private IMediaUrlSigner Signer { get; set; } = default!;

    [Inject]
    private ILogger<MediaRenderer> Logger { get; set; } = default!;

    /// <summary>The picked item's id, or null when nothing is picked.</summary>
    protected int? MediaId { get; private set; }

    /// <summary>The responsive picture, or null when this placement does not render as one.</summary>
    protected ResponsiveImage? Picture { get; private set; }

    /// <summary>A signed link to the stored original, for formats and kinds that have no rendition.</summary>
    protected string? OriginalUrl { get; private set; }

    /// <summary>The resolved item, or null when nothing could be resolved.</summary>
    protected ResolvedMedia? Item { get; private set; }

    /// <summary>
    /// The alternative text to emit; empty for a decorative image and for one nobody has described.
    /// </summary>
    protected string Alt { get; private set; } = string.Empty;

    /// <summary>The caption a template may render beneath the picture.</summary>
    protected string? Caption => Item?.Caption;

    /// <summary>Whether this is the page's likely Largest Contentful Paint image.</summary>
    protected bool IsLcpCandidate { get; private set; }

    /// <summary>How the browser should load the image.</summary>
    /// <remarks>
    /// Everything but the first image is lazy, which is the whole saving: a gallery further down a
    /// long page costs nothing until it is scrolled to. The first image is eager and high priority
    /// because it is almost certainly the element the Largest Contentful Paint is measured against,
    /// and lazy-loading it would delay the one paint the metric watches (spec section 13.6).
    /// </remarks>
    protected string Loading => IsLcpCandidate ? "eager" : "lazy";

    /// <summary>The fetch priority, set only on the image worth prioritising.</summary>
    protected string? FetchPriority => IsLcpCandidate ? "high" : null;

    /// <summary>
    /// The decoding hint, which is <c>async</c> for everything the browser is not waiting on.
    /// </summary>
    /// <remarks>
    /// Deliberately absent on the LCP image. <c>decoding="async"</c> tells the browser it may present
    /// the rest of the page before this picture has been decoded, which is exactly the wrong
    /// instruction for the element whose paint is being measured.
    /// </remarks>
    protected string? Decoding => IsLcpCandidate ? null : "async";

    /// <summary>The label a document link carries.</summary>
    protected string DocumentLabel =>
        Item?.Title is { Length: > 0 } title ? title : Item?.OriginalFileName ?? string.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// Claiming the LCP slot happens here, synchronously, rather than in
    /// <see cref="OnParametersSetAsync"/>: components begin rendering in document order and stop
    /// being ordered at their first <c>await</c>, so a claim made after the resolve would go to
    /// whichever image's query returned first (see <see cref="RenderContext.ClaimLcpImage"/>).
    /// <para>
    /// The cost of claiming that early is that the slot goes to the first media <em>placement</em>
    /// rather than to the first placement that turns out to be a picture — so a page whose first
    /// media field holds a PDF spends the hint on a download link. That is the better of the two
    /// mistakes: the alternative is a claim made in query-completion order, which would eager-load
    /// an image halfway down the page and compete with the one actually being measured.
    /// </para>
    /// </remarks>
    protected override void OnParametersSet()
    {
        MediaId = IdMember(MediaIdMember);
        Picture = null;
        OriginalUrl = null;
        Item = null;
        Alt = string.Empty;

        if (MediaId is not { } mediaId) return;

        Context?.CacheTags.AddMedia(mediaId);

        IsLcpCandidate = Context?.ClaimLcpImage() ?? false;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (MediaId is not { } mediaId) return;

        var resolved = await Media.ResolveAsync([mediaId], CancellationToken.None);

        if (!resolved.TryGetValue(mediaId, out var item))
        {
            Logger.LogWarning(
                "Media item {MediaId} placed in '{PropertyKey}' on page {PageId} version {VersionId} " +
                "does not exist or is in the recycle bin; the placement renders its alternative text.",
                mediaId,
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            // The placement's own alternative text is the only thing left that describes what was
            // meant to be here, so the placeholder carries it rather than rendering nothing.
            Alt = StringMember(AltOverrideMember) ?? string.Empty;

            return;
        }

        Item = item;

        if (ResponsiveImages.Alt(item, StringMember(AltOverrideMember)) is { } alt)
        {
            Alt = alt;
        }
        else
        {
            // Neither described nor declared decorative. Publishing a page in this state fails
            // validation (task P5-21), so reaching it at render time means the content predates the
            // rule or was published with it configured down to a warning — either way the honest
            // markup is an empty alt plus a log entry, never a guess at what the picture shows.
            Alt = string.Empty;

            Logger.LogWarning(
                "Media item {MediaId} placed in '{PropertyKey}' on page {PageId} version {VersionId} " +
                "has neither alternative text nor a decorative flag; it renders with an empty alt.",
                mediaId,
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);
        }

        if (item.Kind is MediaKind.Image)
        {
            Picture = ResponsiveImages.Build(
                item,
                Signer,
                Crop(),
                FocalPoint(),
                Configuration.GetString(SizesSetting));
        }

        // Everything with no rendition — an SVG, an animated GIF, a document, an item whose
        // dimensions were never recorded — is linked at its signed original instead. The signature
        // matters as much here as on a rendition: without it the whole library would be enumerable
        // by id (spec section 13.5).
        if (Picture is null)
        {
            OriginalUrl = Signer.BuildOriginalUrl(item.Id, item.EditsVersion, item.OriginalFileName);
        }
    }

    /// <summary>Reads this placement's crop, treating anything unusable as absent.</summary>
    /// <remarks>
    /// Unusable geometry renders the whole picture rather than failing. The validator already
    /// refused this shape on write and reported it against the property; refusing it again on the
    /// delivery path would turn one editor-visible diagnostic into a broken image per request
    /// (spec section 15.3).
    /// </remarks>
    private NormalizedRect? Crop() =>
        Member(CropMember) is { ValueKind: JsonValueKind.Object } crop &&
        Fraction(crop, "x") is { } x &&
        Fraction(crop, "y") is { } y &&
        Fraction(crop, "w") is { } width &&
        Fraction(crop, "h") is { } height &&
        new NormalizedRect(x, y, width, height) is { IsValid: true } rect
            ? rect
            : null;

    /// <summary>Reads this placement's focal point, treating anything unusable as absent.</summary>
    private NormalizedPoint? FocalPoint() =>
        Member(FocalPointMember) is { ValueKind: JsonValueKind.Object } focal &&
        Fraction(focal, "x") is { } x &&
        Fraction(focal, "y") is { } y &&
        new NormalizedPoint(x, y) is { IsValid: true } point
            ? point
            : null;

    private static double? Fraction(JsonElement geometry, string member) =>
        geometry.TryGetProperty(member, out var value) &&
        value.ValueKind is JsonValueKind.Number &&
        value.TryGetDouble(out var fraction)
            ? fraction
            : null;
}
