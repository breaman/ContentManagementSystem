using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// The pinned-version affordance: which of a page's placements have stopped following their item,
/// and the action that puts them back (task P4-05, spec section 9.2).
/// </summary>
/// <remarks>
/// Pinning is the escape hatch for content that must be reproducible under audit, and it is
/// invisible by construction — a pinned footer looks exactly like a following one until the item is
/// republished and this page alone does not change. Acceptance criterion P4 #3 asks for a badge and
/// an "update to latest" action because without them the only way to discover a stale pin is to
/// notice that a change failed to appear.
/// <para>
/// It lives on the <em>page</em> editor rather than on the item's, and that is the whole point of
/// where a pin is stored: the pin is a property of the placement, so the person who can clear it is
/// the person editing the page, not the person publishing the item.
/// </para>
/// <para>
/// Clearing a pin rewrites the placement's <c>pinnedVersionId</c> to null and writes the draft. It
/// deliberately does not publish: adopting a newer version of shared content on a live page is a
/// publish somebody performs, not a side effect of tidying up a badge.
/// </para>
/// </remarks>
public partial class PinnedPlacements : ComponentBase
{
    /// <summary>The page whose draft holds the placements.</summary>
    [Parameter]
    [EditorRequired]
    public PageDetail? Page { get; set; }

    /// <summary>Raised after the draft has been rewritten, so the editor can reload.</summary>
    [Parameter]
    public EventCallback OnUpdated { get; set; }

    /// <summary>Reads and writes pages, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Names the pinned items so the list says what an editor recognises.</summary>
    [Inject]
    private IReusableClient Reusable { get; set; } = default!;

    /// <summary>The pinned placements found in the draft, in zone order.</summary>
    protected IReadOnlyList<PinnedPlacement> Placements { get; private set; } = [];

    /// <summary>Whether the rewrite is in flight.</summary>
    protected bool IsBusy { get; private set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Placements = [];

        if (Page is null || !ContentPayload.TryParse(Page.ContentJson, out var payload)) return;

        var found = new List<PinnedPlacement>();

        foreach (var zoneKey in payload.ZoneKeys)
        {
            if (!payload.TryGetZone(zoneKey, out var zone)) continue;

            if (Read(zone) is not { } placement) continue;

            found.Add(placement with { ZoneKey = zoneKey });
        }

        // Named in one pass afterwards rather than inside the loop: a page can pin several
        // placements, and a lookup per zone is the N+1 that only shows up on the page that has most
        // of them.
        Placements = await NameAsync(found);
    }

    /// <summary>
    /// Reads a stored value that is a pinned reusable placement.
    /// </summary>
    /// <returns>The placement, or null when the value is not one or is not pinned.</returns>
    /// <remarks>
    /// Read from the payload rather than from the captured schema, following the rule the whole
    /// content model obeys: a value is interpreted by whatever wrote it. A zone whose field type was
    /// changed under stored content still holds a placement, and the pin on it still matters.
    /// </remarks>
    private static PinnedPlacement? Read(JsonElement zone)
    {
        if (zone.ValueKind is not JsonValueKind.Object) return null;

        if (!zone.TryGetProperty(ContentPayloadMembers.Type, out var type) ||
            type.ValueKind is not JsonValueKind.String ||
            type.GetString() != FieldTypeKeys.Reusable)
        {
            return null;
        }

        if (!TryReadId(zone, "reusableContentId", out var reusableContentId)) return null;

        return TryReadId(zone, "pinnedVersionId", out var pinnedVersionId)
            ? new PinnedPlacement(string.Empty, reusableContentId, pinnedVersionId, null, false)
            : null;
    }

    private static bool TryReadId(JsonElement owner, string member, out int id)
    {
        id = 0;

        return owner.TryGetProperty(member, out var value) &&
            value.ValueKind is JsonValueKind.Number &&
            value.TryGetInt32(out id) &&
            id > 0;
    }

    /// <summary>Fills in each item's display name and whether the pin has fallen behind.</summary>
    private async Task<IReadOnlyList<PinnedPlacement>> NameAsync(List<PinnedPlacement> placements)
    {
        if (placements.Count == 0) return [];

        var named = new List<PinnedPlacement>(placements.Count);

        foreach (var placement in placements)
        {
            var item = await Reusable.GetAsync(placement.ReusableContentId);

            if (item is null)
            {
                // The item is gone. Still listed, because a pin to nothing renders nothing and is
                // exactly the state an editor needs to be shown rather than left to discover.
                named.Add(placement);

                continue;
            }

            var versions = await Reusable.GetVersionsAsync(placement.ReusableContentId);
            var published = versions.FirstOrDefault(version => version.IsPublished);

            named.Add(placement with
            {
                Name = item.Summary.Name,
                IsStale = published is not null && published.Id != placement.PinnedVersionId,
            });
        }

        return named;
    }

    /// <summary>Clears every pin on the page and saves the draft.</summary>
    /// <remarks>
    /// All of them at once rather than one at a time. A page with several pinned placements almost
    /// always got that way from one duplication or one import, and offering them individually would
    /// invite an editor to clear three of four and believe they were done.
    /// </remarks>
    private async Task UpdateAllAsync()
    {
        if (Page is null || Placements.Count == 0) return;

        IsBusy = true;

        try
        {
            if (JsonNode.Parse(Page.ContentJson) is not JsonObject document ||
                document[ContentPayloadMembers.Zones] is not JsonObject zones)
            {
                return;
            }

            foreach (var placement in Placements)
            {
                if (zones[placement.ZoneKey] is JsonObject stored)
                {
                    // Set to null rather than removed. Absent and null are different facts about a
                    // payload, and a placement that once named a version says something by saying it
                    // no longer does.
                    stored["pinnedVersionId"] = null;
                }
            }

            var saved = await Client.SaveDraftAsync(
                Page.Summary.Id,
                new SaveDraftRequest(document.ToJsonString(), Page.RowVersion));

            if (saved.IsSuccess) await OnUpdated.InvokeAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>One placement on this page that names an exact version of a shared item.</summary>
    /// <param name="ZoneKey">Zone holding the placement.</param>
    /// <param name="ReusableContentId">The item placed.</param>
    /// <param name="PinnedVersionId">The version it is pinned to.</param>
    /// <param name="Name">The item's display name, or null when the item no longer exists.</param>
    /// <param name="IsStale">Whether a newer version of the item is published.</param>
    public sealed record PinnedPlacement(
        string ZoneKey,
        int ReusableContentId,
        int PinnedVersionId,
        string? Name,
        bool IsStale);
}
