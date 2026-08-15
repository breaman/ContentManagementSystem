using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>reusable</c> value — a placement of an independently published content item
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// Nothing renders until P4 supplies the item store, and that is already the behaviour spec section
/// 15.3 specifies for a placement that cannot be resolved: render nothing, log a warning, and let
/// the broken-references report pick it up.
/// <para>
/// The <c>ru:{id}</c> cache tag is added regardless, for the reason <see cref="MediaRenderer"/> adds
/// its own: the tag is how one publish of a shared banner updates forty pages without republishing
/// any of them, and a page that rendered before the tag existed would never be evicted by it.
/// </para>
/// <para>
/// The tag names the item, not the pinned version, even when the placement pins one. A pinned
/// placement does not follow the item's publishes, but it does still have to be evicted when that
/// version is deleted or the item is removed, and one tag per item keeps the eviction side from
/// having to know which placements pinned what.
/// </para>
/// </remarks>
public partial class ReusableRenderer : CmsFieldRendererBase
{
    /// <summary>The reusable item the placement names.</summary>
    private const string ReusableContentIdMember = "reusableContentId";

    [Inject]
    private ILogger<ReusableRenderer> Logger { get; set; } = default!;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (IdMember(ReusableContentIdMember) is not { } reusableId) return;

        Context?.CacheTags.AddReusable(reusableId);

        Logger.LogWarning(
            "Reusable content {ReusableId} placed in '{PropertyKey}' on page {PageId} version " +
            "{VersionId} could not be resolved; it renders nothing.",
            reusableId,
            PropertyKey,
            Context?.Page.Id,
            Context?.Page.VersionId);
    }
}
