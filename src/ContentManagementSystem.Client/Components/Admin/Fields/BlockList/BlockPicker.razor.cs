using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.BlockList;

/// <summary>
/// Chooses which kind of block to add (task P6-06).
/// </summary>
/// <remarks>
/// The list is already constrained by the time it gets here: the editor intersects the property's
/// <c>allowedBlockTypes</c> with what this deployment has registered, and drops orphaned types. A
/// picker that offered a type the property refuses would author content the publish check then
/// rejects, and one that offered an orphaned type would author content the site draws as nothing.
/// <para>
/// A double-click adds immediately, which is what a practised author reaches for; single-click plus
/// the button is what makes the same thing reachable by keyboard, since a dialog's confirm button is
/// where focus already is.
/// </para>
/// </remarks>
public partial class BlockPicker : ComponentBase
{
    /// <summary>Whether the picker is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>The block types this property will accept, in the order to offer them.</summary>
    [Parameter]
    public IReadOnlyList<BlockTypeSummary> BlockTypes { get; set; } = [];

    /// <summary>Raised with the chosen block type.</summary>
    [Parameter]
    public EventCallback<BlockTypeSummary> OnPicked { get; set; }

    /// <summary>Raised when the editor backs out.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>What has been chosen but not yet confirmed.</summary>
    private BlockTypeSummary? Selected { get; set; }

    private Task ConfirmAsync() => Selected is { } type ? ChooseAsync(type) : Task.CompletedTask;

    private async Task ChooseAsync(BlockTypeSummary type)
    {
        Selected = null;

        await OnPicked.InvokeAsync(type);
    }
}
