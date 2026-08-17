using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Common;

/// <summary>
/// Announces something to a screen reader without putting it on the screen (task P6-22,
/// spec section 28).
/// </summary>
/// <remarks>
/// The backoffice reports two kinds of thing this way, and they need different urgencies. Autosave
/// state is <em>polite</em>: it matters, and it can wait for the current sentence to finish, because
/// interrupting somebody mid-word to say "saved" is worse than saying it a second later. A
/// validation result is <em>assertive</em>: it arrives because a person pressed a button and is
/// waiting to hear what happened, and a polite announcement queued behind their own typing would
/// reach them after they had moved on.
/// <para>
/// <strong>What it must never do is announce on every render.</strong> The message is set when
/// something has actually changed — a phase crossing, a check completing — rather than derived from
/// state that redraws on every keystroke; a region wired to the latter reads the whole screen aloud
/// while somebody types.
/// </para>
/// </remarks>
public partial class LiveRegion : ComponentBase
{
    /// <summary>What to announce, or null or empty to announce nothing.</summary>
    [Parameter]
    public string? Message { get; set; }

    /// <summary>Whether to interrupt, rather than waiting for a pause.</summary>
    [Parameter]
    public bool IsAssertive { get; set; }
}
