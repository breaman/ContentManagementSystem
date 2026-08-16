using ContentManagementSystem.Core.Delivery;
using ContentManagementSystem.Core.Fields.Types;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>reusable</c> value — a placement of an independently published content item
/// (tasks P4-04 and P4-05, spec sections 7.1 and 9).
/// </summary>
/// <remarks>
/// The placement stores an item id and, optionally, a version to pin to. Everything else is resolved
/// here, at the moment the page is served, which is the mechanism behind goal G4: an unpinned
/// placement reads whatever version of the item is published <em>now</em>, so one publish of a shared
/// banner changes forty pages without any of them being republished
/// (acceptance criterion P4 #2).
/// <para>
/// <strong>The <c>ru:{id}</c> cache tag is added before the resolve and whatever the outcome.</strong>
/// A page that rendered nothing because the item was unpublished must still be evicted when the item
/// is published, and a tag added only on success would leave that page stale forever — the failure
/// mode of spec section 7.3, arriving silently. The tag names the item and never the pinned version,
/// so the eviction side never has to know which placements pinned what.
/// </para>
/// <para>
/// Every way a placement can fail to resolve renders nothing and logs, per spec section 15.3, and the
/// reason is logged distinctly: an editor's broken-references report has different remedies for an
/// item that was never published, one that was deleted, and a pin left behind by a pruned version.
/// </para>
/// </remarks>
public partial class ReusableRenderer : CmsFieldRendererBase
{
    [Inject]
    private IReusableContentResolver Resolver { get; set; } = default!;

    [Inject]
    private ICmsComponentCatalog Components { get; set; } = default!;

    [Inject]
    private ILogger<ReusableRenderer> Logger { get; set; } = default!;

    /// <summary>
    /// The items already being rendered above this placement.
    /// </summary>
    /// <remarks>
    /// Absent at the top of a page, where nothing is above it. Nullable rather than defaulted by the
    /// host so that a template rendered outside the delivery host still works: a missing chain means
    /// depth zero, which is the truth.
    /// </remarks>
    [CascadingParameter]
    public ReusableResolutionChain? AncestorChain { get; set; }

    /// <summary>What the resolver made of the placement.</summary>
    protected ResolvedReusableContent Resolved { get; private set; } =
        ResolvedReusableContent.Unresolved(ReusableResolutionStatus.NotFound, 0);

    /// <summary>The chain as seen by anything nested inside the resolved item.</summary>
    protected ReusableResolutionChain Chain { get; private set; } = ReusableResolutionChain.Root;

    /// <summary>The component declaring the item's block type, when one is deployed.</summary>
    protected Type? ComponentType { get; private set; }

    /// <summary>The item's content, presented as the block context its component reads.</summary>
    protected BlockRenderContext? Block { get; private set; }

    /// <summary>Parameters handed to the block component.</summary>
    protected Dictionary<string, object?> Parameters { get; private set; } = [];

    /// <summary>
    /// Whether the editor's badge is rendered above the item.
    /// </summary>
    /// <remarks>
    /// Preview only, like every other editorial affordance in this pipeline. It carries the item's
    /// name and, for a pinned placement that has fallen behind, the version it is stuck on — which is
    /// what task P4-05 asks the editor to show beside its "update to latest" action.
    /// </remarks>
    protected bool ShowsBadge => Context?.IsPreview ?? false;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Resolved = ResolvedReusableContent.Unresolved(ReusableResolutionStatus.NotFound, 0);
        ComponentType = null;
        Block = null;
        Parameters = [];

        if (IdMember(ReusableFieldType.ReusableContentIdMember) is not { } reusableId) return;

        // Before the resolve, and unconditionally. See the class remarks.
        Context?.CacheTags.AddReusable(reusableId);

        var ancestors = AncestorChain ?? ReusableResolutionChain.Root;

        Resolved = await Resolver.ResolveAsync(
            reusableId,
            IdMember(ReusableFieldType.PinnedVersionIdMember),
            ancestors,
            Context?.IsPreview ?? false,
            CancellationToken.None);

        if (!Resolved.IsResolved)
        {
            LogUnresolved(reusableId);

            return;
        }

        if (!Components.TryGetBlockType(Resolved.BlockTypeKey, out var componentType))
        {
            // The same rule an inline block of a retired type follows: skipped, logged, and the page
            // around it unaffected (acceptance criterion P3 #9).
            Logger.LogWarning(
                "No component declares block type '{BlockTypeKey}', which shapes reusable item " +
                "{ReusableId} '{Key}' placed in '{PropertyKey}' on page {PageId} version {VersionId}; " +
                "it renders nothing.",
                Resolved.BlockTypeKey,
                reusableId,
                Resolved.Key,
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return;
        }

        Chain = ancestors.Push(reusableId);
        ComponentType = componentType;

        Block = new BlockRenderContext(
            BlockIds.ForReusableVersion(Resolved.VersionId),
            Resolved.BlockTypeKey,
            Resolved.BlockTypeRevision,
            Resolved.Properties,
            Resolved.Schema);

        Parameters = BlockParameters.For(componentType, Block);
    }

    /// <summary>Reports why a placement rendered nothing, distinctly enough to act on.</summary>
    private void LogUnresolved(int reusableId)
    {
        var (reason, remedy) = Resolved.Status switch
        {
            ReusableResolutionStatus.NotPublished =>
                ("is not published", "publish the item, or remove the placement"),
            ReusableResolutionStatus.PinnedVersionMissing =>
                ("pins a version that no longer exists or belongs to another item",
                    "use the placement's 'update to latest' action"),
            ReusableResolutionStatus.Cycle =>
                ("closes a loop back to an item already being rendered", "break the loop in the item's content"),
            ReusableResolutionStatus.TooDeep =>
                ($"nests deeper than the {ReusableResolutionChain.MaxDepth} levels delivery follows",
                    "flatten the nesting"),
            _ => ("no longer exists or is in the recycle bin", "restore the item, or remove the placement"),
        };

        Logger.LogWarning(
            "Reusable item {ReusableId} placed in '{PropertyKey}' on page {PageId} version " +
            "{VersionId} {Reason}; it renders nothing. To fix it, {Remedy}.",
            reusableId,
            PropertyKey,
            Context?.Page.Id,
            Context?.Page.VersionId,
            reason,
            remedy);
    }
}
