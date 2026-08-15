using System.Text.Json;

using ContentManagementSystem.Core.Routing;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>pageReference</c> value, single or multiple (spec section 7.1).
/// </summary>
/// <remarks>
/// Like <see cref="LinkRenderer"/> this stores identity and resolves late (decision D6), but it
/// carries no text of its own: the label is the target's <em>current</em> title, so renaming a page
/// updates every list that points at it without republishing any of them.
/// <para>
/// <strong>Resolved in one call, whatever the count.</strong> A related-articles list holds a dozen
/// ids, and a resolver called once per id is the N+1 that only shows up under real content — which
/// is why <see cref="ILinkResolver"/> takes a set rather than an id.
/// </para>
/// <para>
/// A reference that resolves to nothing is dropped from the output and logged. Dropping loses a row
/// from a list, which is recoverable and visible in the broken-references report; the alternative,
/// an anchor with an empty <c>href</c>, navigates the reader to the page they are already on.
/// </para>
/// </remarks>
public partial class PageReferenceRenderer : CmsFieldRendererBase
{
    [Inject]
    private ILinkResolver Links { get; set; } = default!;

    [Inject]
    private ILogger<PageReferenceRenderer> Logger { get; set; } = default!;

    /// <summary>The referenced pages that resolved, in the order they were authored.</summary>
    protected IReadOnlyList<ResolvedLink> Targets { get; private set; } = [];

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Targets = [];

        var pageIds = StoredIds();

        if (pageIds.Count == 0) return;

        // Tagged before resolution, and including the ids that fail to resolve: a reference to a
        // page that is not published yet must re-render when it is, and a tag added only on success
        // would leave that page's own publish unable to reach this one.
        foreach (var pageId in pageIds)
        {
            Context?.CacheTags.AddPage(pageId);
        }

        var includeUnpublished = Context?.IsPreview ?? false;
        var resolved = await Links.ResolveAsync(pageIds, includeUnpublished, CancellationToken.None);

        var targets = new List<ResolvedLink>(pageIds.Count);

        foreach (var pageId in pageIds)
        {
            if (resolved.TryGetValue(pageId, out var target) && target.Url is { Length: > 0 })
            {
                targets.Add(target);

                continue;
            }

            Logger.LogWarning(
                "Page reference in '{PropertyKey}' on page {PageId} version {VersionId} points at " +
                "page {TargetId}, which has no URL this audience may see; it is omitted.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId,
                pageId);
        }

        Targets = targets;
    }

    /// <summary>Badges an unpublished target, which only preview ever resolves.</summary>
    /// <param name="target">The resolved reference.</param>
    /// <returns>The anchor's classes.</returns>
    protected static string CssClass(ResolvedLink target)
    {
        ArgumentNullException.ThrowIfNull(target);

        return target.IsPublished ? "cms-page-reference" : "cms-page-reference cms-link-draft";
    }

    /// <summary>
    /// Reads the ids under the one member both shapes use.
    /// </summary>
    /// <remarks>
    /// Single and multiple are the same member, following <c>choice</c>, so this branches on what is
    /// stored rather than on the <c>multiple</c> setting — a property narrowed to single selection
    /// still has pages holding arrays.
    /// </remarks>
    private List<int> StoredIds()
    {
        if (IdMember(ValueMember) is { } single) return [single];

        if (ArrayMember(ValueMember) is not { } items) return [];

        var ids = new List<int>(items.GetArrayLength());

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.Number && item.TryGetInt32(out var id) && id > 0)
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
