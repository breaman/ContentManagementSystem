using System.Globalization;
using System.Text;

using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Media;

namespace ContentManagementSystem.Core.Media.Delivery;

/// <summary>One <c>&lt;source&gt;</c> of a <c>&lt;picture&gt;</c>.</summary>
/// <param name="MimeType">The <c>type</c> attribute, such as <c>image/webp</c>.</param>
/// <param name="SrcSet">The candidate URLs with their <c>w</c> descriptors.</param>
public sealed record ResponsiveImageSource(string MimeType, string SrcSet);

/// <summary>
/// Everything the <c>media</c> renderer needs to emit one picture (task P5-20, spec section 13.6).
/// </summary>
/// <param name="Sources">Format-specific candidates, best first. Empty for a format with no renditions.</param>
/// <param name="Src">The <c>src</c> of the fallback <c>&lt;img&gt;</c>.</param>
/// <param name="SrcSet">The fallback format's own candidates, or null when there is only one.</param>
/// <param name="Sizes">The <c>sizes</c> attribute both the sources and the image carry.</param>
/// <param name="Width">Intrinsic width of the fallback image, in pixels.</param>
/// <param name="Height">Intrinsic height of the fallback image, in pixels.</param>
/// <remarks>
/// A plain record rather than markup, so the arithmetic that produces it — which is where the
/// mistakes are — is unit-testable without rendering a component, and so the same numbers could feed
/// a different markup shape later without being derived twice.
/// </remarks>
public sealed record ResponsiveImage(
    IReadOnlyList<ResponsiveImageSource> Sources,
    string Src,
    string? SrcSet,
    string Sizes,
    int Width,
    int Height);

/// <summary>
/// Builds the responsive markup model for a placed image (task P5-20, spec section 13.6).
/// </summary>
/// <remarks>
/// <strong>Every URL here is signed, and that is the only reason this can exist.</strong> The
/// delivery endpoint refuses an unsigned size, so a <c>srcset</c> is the one place arbitrary
/// dimensions are legitimately chosen — by the server, from an allowlist, while rendering
/// (spec section 13.5). An editor never sees a signature and no client ever constructs one.
/// <para>
/// Three properties of the output are load-bearing rather than cosmetic:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>width</c> and <c>height</c> are the <em>resolved</em> dimensions of the fallback rendition, not
/// the requested ones. Padding aside, the pipeline never upscales, so a 900 px original asked for at
/// 1280 comes back at 900 — and markup that claimed 1280 would reserve a box the picture never
/// fills, which is the layout shift the attributes exist to prevent.
/// </description></item>
/// <item><description>
/// Each <c>w</c> descriptor is the width the browser will actually receive, for the same reason. A
/// descriptor that overstated its candidate would make the browser's density arithmetic wrong and
/// have it choose the larger file on a screen that needed the smaller one.
/// </description></item>
/// <item><description>
/// The candidate set is drawn from <see cref="RenditionSpec.AllowedWidths"/> and nowhere else. The
/// signature already makes an arbitrary width unrequestable by an outsider; the allowlist is what
/// stops the <em>application</em> asking for a thousand distinct encodes of one photograph.
/// </description></item>
/// </list>
/// </remarks>
public static class ResponsiveImages
{
    /// <summary>The <c>sizes</c> attribute used when a placement configures none.</summary>
    /// <remarks>
    /// <c>100vw</c> — the honest default. Without a <c>sizes</c> attribute a browser assumes exactly
    /// this anyway, so stating it changes nothing and makes the value a template author can see and
    /// override. Overstating it (say <c>800px</c>) would be worse than saying nothing: a browser
    /// would fetch a file too small for a full-width hero and there would be no symptom other than a
    /// soft picture.
    /// </remarks>
    public const string DefaultSizes = "100vw";

    /// <summary>Tallest rendition the delivery endpoint will accept, mirroring <see cref="RenditionSpec.IsAllowed"/>.</summary>
    private const int MaxHeight = 4320;

    /// <summary>
    /// Builds the picture for one placement.
    /// </summary>
    /// <param name="item">The resolved library item.</param>
    /// <param name="signer">Signs each candidate URL.</param>
    /// <param name="usageCrop">This placement's crop, or null when it takes the whole picture.</param>
    /// <param name="usageFocalPoint">This placement's focal point, or null to use the item's own.</param>
    /// <param name="sizes">The <c>sizes</c> attribute, or null for <see cref="DefaultSizes"/>.</param>
    /// <returns>The picture, or null when the item cannot be rendered as one.</returns>
    /// <remarks>
    /// Null is returned for anything the rendition pipeline cannot produce: a document, an item with
    /// no recorded dimensions, or an image in a format the processor decodes but cannot re-encode.
    /// The renderer falls back to a link or to the spec section 15.3 placeholder rather than emitting
    /// an <c>&lt;img&gt;</c> whose <c>src</c> would answer 400.
    /// </remarks>
    public static ResponsiveImage? Build(
        ResolvedMedia item,
        IMediaUrlSigner signer,
        NormalizedRect? usageCrop = null,
        NormalizedPoint? usageFocalPoint = null,
        string? sizes = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(signer);

        if (item.Kind is not MediaKind.Image) return null;

        if (FallbackFormat(item.ContentType) is not { } fallbackFormat) return null;

        if (MediaGeometry.Effective(item.Width, item.Height, item.Edits, usageCrop) is not { } source)
        {
            return null;
        }

        var candidates = Candidates(source);

        if (candidates.Count == 0) return null;

        // The focal point travels in the spec even though the requested boxes share the source's
        // aspect ratio, so rounding on a long thin image steers the trimmed row towards the subject
        // rather than the middle. It also means the signature covers it, which is what stops a
        // client moving it (spec section 13.5).
        var focalPoint = usageFocalPoint ?? item.Edits.FocalPoint;

        var largest = candidates[^1];

        // WebP only. AVIF would belong here and is not produced in v1 — SkiaSharp cannot encode it,
        // and a <source> advertising a format the endpoint refuses would leave browsers that prefer
        // it with no picture at all (spec section 13.9.1).
        var sources = new List<ResponsiveImageSource>(1)
        {
            new("image/webp", SrcSet(item, signer, candidates, ImageOutputFormat.Webp, usageCrop, focalPoint)),
        };

        return new ResponsiveImage(
            sources,
            Url(item, signer, largest, fallbackFormat, usageCrop, focalPoint),
            candidates.Count > 1
                ? SrcSet(item, signer, candidates, fallbackFormat, usageCrop, focalPoint)
                : null,
            string.IsNullOrWhiteSpace(sizes) ? DefaultSizes : sizes.Trim(),
            largest.Output.Width,
            largest.Output.Height);
    }

    /// <summary>
    /// The alternative text an <c>&lt;img&gt;</c> carries, and whether there is any.
    /// </summary>
    /// <param name="item">The resolved library item.</param>
    /// <param name="altOverride">The placement's own alternative text, or null.</param>
    /// <returns>
    /// The text to emit, which is the empty string for a decorative image, or null when the item has
    /// none and was never flagged decorative.
    /// </returns>
    /// <remarks>
    /// The three states of spec section 13.7 kept distinct, because two of them look identical in
    /// markup and mean opposite things. A decorative image emits <c>alt=""</c>, which tells a screen
    /// reader to skip it. An image nobody has described yet must not emit the same thing — that would
    /// launder a missing description into a deliberate one — so it answers null and the renderer logs
    /// and falls back. Publishing such a page fails validation, which is where the editor is meant to
    /// find out (task P5-21).
    /// <para>
    /// The placement's override wins over the item's own text, and only over the item's own text: an
    /// override cannot make a decorative image described, because <c>IsDecorative</c> is a statement
    /// about the picture rather than about one page's use of it.
    /// </para>
    /// </remarks>
    public static string? Alt(ResolvedMedia item, string? altOverride)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.IsDecorative) return string.Empty;

        if (!string.IsNullOrWhiteSpace(altOverride)) return altOverride;

        return string.IsNullOrWhiteSpace(item.AltText) ? null : item.AltText;
    }

    /// <summary>
    /// The output format a client that cannot read WebP is served.
    /// </summary>
    /// <param name="contentType">The item's sniffed media type.</param>
    /// <returns>The format, or null when the item has no rendition pipeline.</returns>
    /// <remarks>
    /// GIF and SVG answer null deliberately, and for different reasons that reach the same place. An
    /// animated GIF put through a still-image encoder comes back as one frame, and SkiaSharp has no
    /// SVG rasterizer at all — so neither has a rendition, and the renderer links the stored original
    /// instead of a picture the endpoint would refuse to produce.
    /// </remarks>
    public static ImageOutputFormat? FallbackFormat(string? contentType) => contentType switch
    {
        "image/png" => ImageOutputFormat.Png,
        "image/jpeg" => ImageOutputFormat.Jpeg,

        // A WebP original has no smaller fallback to offer, so JPEG is what a client that cannot
        // read WebP gets. The delivery endpoint performs the same substitution on Accept, so the two
        // agree about what an old browser is served.
        "image/webp" => ImageOutputFormat.Jpeg,
        _ => null,
    };

    /// <summary>One candidate rendition: what was asked for and what comes back.</summary>
    /// <param name="Requested">The box the URL names.</param>
    /// <param name="Output">The dimensions the pipeline resolves that box to.</param>
    private readonly record struct Candidate(PixelSize Requested, PixelSize Output);

    /// <summary>
    /// Chooses the widths to offer for a source of a given size.
    /// </summary>
    /// <param name="source">The effective size of the placement.</param>
    /// <returns>The candidates, smallest first, with no two resolving to the same width.</returns>
    /// <remarks>
    /// Every allowlisted width up to the source's own, plus the first one above it. That last
    /// candidate looks like an upscale and is not: the pipeline clamps to the source, so asking for
    /// 640 from a 400 px original returns 400 — which is how the full-resolution picture stays
    /// reachable at all when its width is not itself on the allowlist.
    /// <para>
    /// Candidates resolving to the same width are collapsed. Two identical entries in a
    /// <c>srcset</c> are two URLs for one picture, and a browser is entitled to fetch either.
    /// </para>
    /// </remarks>
    private static List<Candidate> Candidates(PixelSize source)
    {
        var candidates = new List<Candidate>(RenditionSpec.AllowedWidths.Count);
        var widths = new HashSet<int>();

        foreach (var width in RenditionSpec.AllowedWidths)
        {
            var height = (int)Math.Round((double)width * source.Height / source.Width);

            if (height is <= 0 or > MaxHeight) continue;

            var requested = new PixelSize(width, height);
            var output = RenditionGeometry.Resolve(source, requested, RenditionMode.Crop);

            if (!widths.Add(output.Width)) continue;

            candidates.Add(new Candidate(requested, output));

            // The first width at or above the source is the last one worth offering: everything
            // larger resolves to the same clamped output and was just filtered out above, and
            // stopping here keeps the srcset short rather than relying on the deduplication.
            if (width >= source.Width) break;
        }

        candidates.Sort((left, right) => left.Output.Width.CompareTo(right.Output.Width));

        return candidates;
    }

    /// <summary>Builds one format's <c>srcset</c>.</summary>
    /// <param name="item">The resolved library item.</param>
    /// <param name="signer">Signs each candidate URL.</param>
    /// <param name="candidates">The widths to offer.</param>
    /// <param name="format">The output format.</param>
    /// <param name="crop">This placement's crop.</param>
    /// <param name="focalPoint">The focal point to steer any trimming by.</param>
    /// <returns>The attribute value.</returns>
    private static string SrcSet(
        ResolvedMedia item,
        IMediaUrlSigner signer,
        List<Candidate> candidates,
        ImageOutputFormat format,
        NormalizedRect? crop,
        NormalizedPoint? focalPoint)
    {
        var builder = new StringBuilder(candidates.Count * 120);

        foreach (var candidate in candidates)
        {
            if (builder.Length > 0) builder.Append(", ");

            builder
                .Append(Url(item, signer, candidate, format, crop, focalPoint))
                .Append(' ')
                .Append(candidate.Output.Width.ToString(CultureInfo.InvariantCulture))
                .Append('w');
        }

        return builder.ToString();
    }

    /// <summary>Signs one candidate URL.</summary>
    private static string Url(
        ResolvedMedia item,
        IMediaUrlSigner signer,
        Candidate candidate,
        ImageOutputFormat format,
        NormalizedRect? crop,
        NormalizedPoint? focalPoint) =>
        signer.BuildUrl(
            new RenditionSpec(
                item.Id,
                candidate.Requested.Width,
                candidate.Requested.Height,
                RenditionMode.Crop,
                format,
                RenditionSpec.DefaultQuality,
                item.EditsVersion,
                focalPoint,
                crop),
            item.OriginalFileName);
}
