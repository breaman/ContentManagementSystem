using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// Renders a structural diff between two page versions (task P2-23).
/// </summary>
/// <remarks>
/// Presentation only — every decision about what changed was made by <c>ContentDiffService</c>. The
/// one thing this component contributes is honouring the distinction the service went to trouble to
/// make: a <see cref="ContentChangeKind.Moved"/> block is drawn as one row saying where it went, not
/// as a removal beside an addition. Drawing it the other way would throw away the only property that
/// makes a diff useful on the edit people make most (acceptance criterion P2 #6).
/// <para>
/// <c>&lt;del&gt;</c> and <c>&lt;ins&gt;</c> carry the meaning semantically rather than through
/// colour alone, so the diff is still readable to a screen reader and to anyone who cannot
/// distinguish the two background tints.
/// </para>
/// </remarks>
public partial class ContentDiffView : ComponentBase
{
    /// <summary>The comparison to render, or null to render nothing.</summary>
    [Parameter]
    public ContentDiff? Diff { get; set; }

    /// <summary>Renders an absent value as a visible placeholder rather than as nothing at all.</summary>
    /// <remarks>
    /// An empty cell reads as "unchanged" when it means "there was nothing here", which is the one
    /// misreading a diff cannot afford.
    /// </remarks>
    private static string Show(string? value) =>
        string.IsNullOrEmpty(value) ? "(empty)" : value;

    /// <summary>A short label for a change, for the badge beside it.</summary>
    private static string Label(ContentChangeKind kind) => kind switch
    {
        ContentChangeKind.Added => "Added",
        ContentChangeKind.Removed => "Removed",
        ContentChangeKind.Changed => "Changed",
        ContentChangeKind.Moved => "Moved",
        _ => "Unchanged",
    };

    private static string BadgeClass(ContentChangeKind kind) => kind switch
    {
        ContentChangeKind.Added => "text-bg-success",
        ContentChangeKind.Removed => "text-bg-danger",
        ContentChangeKind.Changed => "text-bg-warning",
        ContentChangeKind.Moved => "text-bg-info",
        _ => "text-bg-light text-dark",
    };
}
