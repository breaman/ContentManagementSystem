namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>media</c> value — one picked image or file (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>The cache tag is the part that is finished.</strong> A page that renders media item 812
/// declares <c>media:812</c> as it renders it, so replacing that image in the library evicts every
/// page showing it (spec section 16.2). Adding the tag later, with the library in P5, would leave
/// every page published before then invisible to invalidation — and nothing would go back and
/// re-render them to fix it. The dependency is therefore declared now, while the thing it points at
/// does not exist yet.
/// <para>
/// The picture itself is P5. Until the library, its renditions, and its URL scheme exist there is no
/// <c>src</c> to emit, and the honest rendering of a media item nothing can resolve is the one spec
/// section 15.3 already specifies for that case: a placeholder carrying the alt text. When P5 lands,
/// this renderer resolves the item and falls back to exactly this markup when it cannot.
/// </para>
/// <para>
/// The alt text shown is the placement's <c>altOverride</c> only. The library item's own alt text is
/// the usual source and lives in P5's table; a placement that overrides it is the one case readable
/// from the payload alone.
/// </para>
/// </remarks>
public partial class MediaRenderer : CmsFieldRendererBase
{
    /// <summary>The identity of the picked item — the member that decides whether anything is picked.</summary>
    private const string MediaIdMember = "mediaId";

    /// <summary>Alternative text overriding the item's own, for this placement only.</summary>
    private const string AltOverrideMember = "altOverride";

    /// <summary>The picked item's id, or null when nothing is picked.</summary>
    protected int? MediaId { get; private set; }

    /// <summary>The placement's alt text override; empty when it carries none.</summary>
    protected string Alt => StringMember(AltOverrideMember) ?? string.Empty;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        MediaId = IdMember(MediaIdMember);

        if (MediaId is { } mediaId)
        {
            Context?.CacheTags.AddMedia(mediaId);
        }
    }
}
