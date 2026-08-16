using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Shared.Content;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Everything one page render is given: the page, its content, who it is for, and the cache tags it
/// accumulates on the way (spec section 15.2).
/// </summary>
/// <remarks>
/// Cascaded once by the delivery host and read by every template, zone, block, and field renderer
/// below it, so nothing in the tree needs a constructor parameter threaded through four levels.
/// <para>
/// <strong>One instance per render.</strong> It holds <see cref="CacheTags"/>, which is written to
/// while rendering; sharing an instance across requests would mix one response's dependencies into
/// another's. The published-content cache (spec section 16.1) caches the <em>page and payload</em>,
/// which are immutable, and a context is built around them per request.
/// </para>
/// <para>
/// A class rather than the spec's <c>record</c>: it carries a mutable accumulator, and value
/// equality over that would be either meaningless or misleading.
/// </para>
/// </remarks>
public sealed class RenderContext
{
    /// <summary>Whether this render's likely Largest Contentful Paint image has been claimed.</summary>
    private int _lcpClaimed;

    /// <summary>
    /// Creates a render context and seeds the cache tags every page response carries.
    /// </summary>
    /// <param name="page">The page version being rendered.</param>
    /// <param name="payload">That version's content.</param>
    /// <param name="mode">Which audience the render is for.</param>
    /// <param name="schema">
    /// The captured schema of the template revision the payload was authored against, when it could
    /// be resolved. Null is a rendering condition, not an error: a revision the database no longer
    /// holds still renders from the payload's own type discriminators, which is what keeps a page
    /// alive after a template was deleted out from under it (spec section 8.5).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> or <paramref name="payload"/> is null.</exception>
    public RenderContext(
        RenderPage page,
        ContentPayload payload,
        CmsRenderMode mode = CmsRenderMode.Live,
        ContentSchema? schema = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(payload);

        Page = page;
        Payload = payload;
        Mode = mode;
        Schema = schema;

        // Seeded here rather than by the delivery endpoint: these two are the tags every page
        // response carries by definition, and a tag that has to be remembered is a tag that will
        // eventually be forgotten on one code path and leave a stale page live.
        CacheTags.Add(Rendering.CacheTags.Page(page.Id));
        CacheTags.Add(Rendering.CacheTags.Template(page.TemplateId));
    }

    /// <summary>
    /// Builds a context around a loaded delivery result.
    /// </summary>
    /// <param name="content">What <c>IPublishedContentService</c> loaded.</param>
    /// <param name="mode">Which audience the render is for.</param>
    /// <returns>The context to hand <see cref="CmsPageHost"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <remarks>
    /// The one supported way to turn loaded content into a render, so that the three things that
    /// have to travel together — the page facts, the payload, and the captured schema the payload
    /// names — cannot be assembled from different sources by a caller in a hurry. A context built
    /// with one version's payload and another's schema would render every value under bounds nobody
    /// chose for it.
    /// <para>
    /// A fresh context, and therefore a fresh cache tag set, per call. The delivery result may come
    /// out of a cache that is shared between requests; this must not.
    /// </para>
    /// </remarks>
    public static RenderContext For(PublishedContent content, CmsRenderMode mode = CmsRenderMode.Live)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new RenderContext(RenderPage.From(content), content.Payload, mode, content.Schema);
    }

    /// <summary>The page version being rendered.</summary>
    public RenderPage Page { get; }

    /// <summary>The content of the version being rendered.</summary>
    public ContentPayload Payload { get; }

    /// <summary>Which audience this render is for.</summary>
    public CmsRenderMode Mode { get; }

    /// <summary>
    /// The captured schema this payload was authored against, or null when it could not be resolved.
    /// </summary>
    /// <remarks>
    /// Rendering reads a value's field type from the <em>payload's</em> discriminator, never from
    /// here — a value has to be read by whatever wrote it. The schema supplies the field
    /// configuration that renderer then needs, and only when the two agree on the field type.
    /// </remarks>
    public ContentSchema? Schema { get; }

    /// <summary>Cache tags accumulated during the render, applied to the response afterwards.</summary>
    public CacheTagSet CacheTags { get; } = new();

    /// <summary>Whether the audience is an editor rather than an anonymous visitor.</summary>
    /// <remarks>
    /// The one question renderers ask often enough to be worth a name: it decides whether an
    /// unpublished link target resolves and is badged, or degrades to plain text.
    /// </remarks>
    public bool IsPreview => Mode is CmsRenderMode.Preview or CmsRenderMode.ScheduledPreview;

    /// <summary>
    /// Claims this render's likely Largest Contentful Paint image, which only the first caller wins.
    /// </summary>
    /// <returns><see langword="true"/> for the first image on the page, and false for every other.</returns>
    /// <remarks>
    /// Spec section 13.6 asks for <c>loading="eager"</c> and <c>fetchpriority="high"</c> on the first
    /// image in the first zone, and lazy loading on the rest. That is a fact about the page rather
    /// than about any one image, so it is settled here — the alternative, a renderer deciding for
    /// itself, cannot work: an image renderer has no idea whether another one already ran.
    /// <para>
    /// <strong>Claimed before the first <c>await</c>, deliberately.</strong> Templates, zones, and
    /// field renderers begin rendering in document order, and a claim made synchronously at the top
    /// of <c>OnParametersSet</c> is therefore made in that order too. A claim made after an
    /// asynchronous resolve would be made in completion order, which is whichever image's database
    /// query returned first — and eager-loading an image halfway down the page is worse than eager-
    /// loading none, because it competes with the one that is actually visible.
    /// </para>
    /// <para>
    /// Interlocked because a render is not guaranteed to be single-threaded; the cost is one atomic
    /// exchange per image, and the failure it prevents is two images both claiming to be the LCP.
    /// </para>
    /// </remarks>
    public bool ClaimLcpImage() => Interlocked.Exchange(ref _lcpClaimed, 1) == 0;
}
