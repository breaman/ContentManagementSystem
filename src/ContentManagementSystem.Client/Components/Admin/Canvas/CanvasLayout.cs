using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>
/// One run of zone cards under a heading, as the editing canvas lays them out (spec section 14.1).
/// </summary>
/// <param name="Name">The group's heading, or null for the zones that declare no group.</param>
/// <param name="Zones">Its zones, in the order an editor sees them.</param>
public sealed record CanvasGroup(string? Name, IReadOnlyList<CapturedSlot> Zones);

/// <summary>
/// Turns a captured revision's zones into the canvas's card groups (task P6-05).
/// </summary>
/// <remarks>
/// Separated from the component because it is the part that is worth stating exactly: the order
/// cards appear in is the one thing about a canvas an editor builds muscle memory for, and a rule
/// expressed only in a Razor <c>foreach</c> can only be checked by rendering one.
/// </remarks>
public static class CanvasLayout
{
    /// <summary>
    /// Groups and orders zones for the canvas.
    /// </summary>
    /// <param name="slots">The zones the draft's template revision captured.</param>
    /// <returns>The groups, in the order they are drawn. Empty when there are no zones.</returns>
    /// <remarks>
    /// Zones are walked in sort order — ties broken by key, so the same template draws the same page
    /// twice running — and a heading is opened whenever the group changes. Two rules bend that:
    /// <list type="number">
    /// <item>a named group already opened is <em>reopened</em>, so its zones are drawn together
    /// wherever their sort orders scattered them;</item>
    /// <item>a run of zones that declare no group is its own headingless section, and is not merged
    /// with the ungrouped zones elsewhere on the page.</item>
    /// </list>
    /// <para>
    /// Rule 1 is what makes a heading meaningful. A group split into two runs because somebody
    /// numbered a zone in the middle of it would show the same heading twice, and an editor
    /// scrolling for "SEO" would stop at the first of them. It costs the case where two groups are
    /// deliberately interleaved, which is not a layout the template editor offers a way to ask for.
    /// </para>
    /// <para>
    /// Rule 2 is the same reasoning read the other way. An unnamed run has no identity to merge on,
    /// and merging on its absence would drag the ungrouped footer of a page up above the SEO group
    /// it was numbered after — a zone moving because of a group it is not in.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<CanvasGroup> Build(IReadOnlyList<CapturedSlot>? slots)
    {
        if (slots is not { Count: > 0 })
        {
            return [];
        }

        var groups = new List<Run>();

        // Ordinal, and trimmed: "SEO" and "SEO " are a template author's stray space rather than two
        // groups, and drawing them as two would be the confusing half of the mistake.
        var named = new Dictionary<string, Run>(StringComparer.Ordinal);

        foreach (var slot in slots
            .OrderBy(slot => slot.SortOrder)
            .ThenBy(slot => slot.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(slot.Group))
            {
                if (groups is not [.., { Name: null } open])
                {
                    groups.Add(open = new Run(null));
                }

                open.Zones.Add(slot);

                continue;
            }

            var heading = slot.Group.Trim();

            if (!named.TryGetValue(heading, out var group))
            {
                named[heading] = group = new Run(heading);

                groups.Add(group);
            }

            group.Zones.Add(slot);
        }

        return [.. groups.Select(group => new CanvasGroup(group.Name, group.Zones))];
    }

    /// <summary>A group while it is still being filled.</summary>
    private sealed class Run(string? name)
    {
        public string? Name { get; } = name;

        public List<CapturedSlot> Zones { get; } = [];
    }
}
