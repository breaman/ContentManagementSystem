using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Marks reusable content inside a preview, and says when a pinned placement has fallen behind
/// (task P4-05, spec section 9.2).
/// </summary>
/// <remarks>
/// Rendered only under preview, like <see cref="CmsDraftBadge"/>, and for the same reason: an
/// anonymous visitor is looking at a footer, not at the fact that it is shared. It exists because
/// the opposite is true for an editor — content that looks like part of this page but changes when
/// somebody else publishes is exactly what an editor needs to be told before they try to edit it
/// here.
/// <para>
/// <strong>"Update to latest" is offered by the backoffice, not by this badge.</strong> The
/// attributes below carry everything that decision needs — pinned or not, the version being
/// rendered, the version that is current — and the backoffice reads them out of the preview frame.
/// Putting the control here would mean an interactive render mode inside the delivery path, which is
/// the one thing static SSR rules out (spec section 5.3).
/// </para>
/// </remarks>
public partial class CmsReusableBadge : ComponentBase
{
    /// <summary>Display name of the item, so an editor knows what to go and edit.</summary>
    [Parameter]
    public string Name { get; set; } = string.Empty;

    /// <summary>Version number actually being rendered here.</summary>
    [Parameter]
    public int VersionNumber { get; set; }

    /// <summary>Whether the placement names this version rather than following the item.</summary>
    [Parameter]
    public bool IsPinned { get; set; }

    /// <summary>Whether the version being rendered is the one currently published.</summary>
    [Parameter]
    public bool IsLatest { get; set; }

    /// <summary>Version an unpinned placement would render, or null while the item has none.</summary>
    [Parameter]
    public int? PublishedVersionNumber { get; set; }

    /// <summary>
    /// Whether a pinned placement has fallen behind the item.
    /// </summary>
    /// <remarks>
    /// The condition the "update to latest" action exists for, and deliberately narrower than "is
    /// pinned": pinning to the version that happens to be current is not stale, and offering to
    /// update it would be an action that does nothing.
    /// </remarks>
    public bool IsStale => IsPinned && !IsLatest;

    /// <summary>What the badge says on hover.</summary>
    private string Title => IsStale
        ? $"Shared content. This page is pinned to version {VersionNumber}; version " +
            $"{PublishedVersionNumber?.ToString() ?? "?"} is published."
        : IsPinned
            ? $"Shared content, pinned to version {VersionNumber}, which is the published one."
            : $"Shared content. Editing it changes every page that shows it.";
}
