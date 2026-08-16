using ContentManagementSystem.Shared.Contracts.Content;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The publishing state and lock badge one tree row shows (task P6-02, spec section 14.2).
/// </summary>
/// <remarks>
/// Icon plus a visually-hidden word, never colour alone — the requirement P6-39 gates. The word is
/// what a screen reader announces and what survives a monochrome print or a red-green colour
/// deficiency; the colour is an accelerant for everyone else.
/// </remarks>
public partial class PageStatusIndicator : ComponentBase
{
    /// <summary>The page whose state is being shown.</summary>
    [Parameter]
    [EditorRequired]
    public PageSummary Page { get; set; } = default!;

    /// <summary>
    /// The current time, against which a scheduled publish is past or future.
    /// </summary>
    /// <remarks>
    /// A parameter rather than an injected clock, so that every row in one render classifies against
    /// the same instant. A tree that read the clock per row could show two pages scheduled for the
    /// same second in two different states.
    /// </remarks>
    [Parameter]
    public DateTimeOffset Now { get; set; }
}
