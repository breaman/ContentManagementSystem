using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Reusable;

/// <summary>
/// Shows what a change to one item would touch — the where-used panel of spec section 9.4
/// (task P4-11).
/// </summary>
/// <remarks>
/// The counts and the list are shown together and mean different things, which is the point of the
/// panel: the count is exact and is what the publish confirmation is judged against, while the list
/// is bounded so a footer on every page of a large site does not return the site.
/// <para>
/// Three states are told apart on each row, because their consequences differ. A published page
/// following the item <em>will change</em>. A published page that pins a version will not, and is
/// still listed because "did my change reach everything?" is answered by the pages it did not reach.
/// A draft-only reference changes nothing today and still blocks a delete, since it becomes a broken
/// published page the moment somebody publishes it.
/// </para>
/// </remarks>
public partial class WhereUsedPanel : ComponentBase
{
    /// <summary>What the change would touch, or null while loading.</summary>
    [Parameter]
    public ReferenceImpact? Impact { get; set; }

    /// <summary>
    /// What the affected pages would do, phrased for the action being considered.
    /// </summary>
    /// <remarks>
    /// Supplied by the caller rather than fixed here, because the same panel answers three different
    /// questions — publishing changes those pages, unpublishing empties them, deleting is refused
    /// because of them — and "40 pages will change" in front of a delete button would be a lie.
    /// </remarks>
    [Parameter]
    public string Verb { get; set; } = "will change";
}
