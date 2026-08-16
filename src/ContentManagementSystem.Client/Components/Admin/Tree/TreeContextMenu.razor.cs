using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Client.Components.Admin.Tree;

/// <summary>
/// The content tree's context menu (task P6-04, spec section 14.2).
/// </summary>
/// <remarks>
/// Opened by a right-click, and equally by <kbd>Shift</kbd>+<kbd>F10</kbd> or the Context Menu key
/// on the focused row — which is the part a menu built only for the pointer leaves out, and the
/// part that decides whether the tree's operations exist at all for a keyboard user (spec section
/// 28).
/// <para>
/// Positioned with fixed coordinates rather than nested inside the row it belongs to. The tree pane
/// scrolls, so a menu inside it would be clipped by the pane the moment it opened near the bottom.
/// </para>
/// </remarks>
public partial class TreeContextMenu : ComponentBase
{
    /// <summary>Whether the menu is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Title of the page the menu acts on, which names the menu.</summary>
    [Parameter]
    public string Title { get; set; } = string.Empty;

    /// <summary>Viewport x-coordinate the menu's top-left corner sits at.</summary>
    [Parameter]
    public double X { get; set; }

    /// <summary>Viewport y-coordinate the menu's top-left corner sits at.</summary>
    [Parameter]
    public double Y { get; set; }

    /// <summary>The entries, already filtered to what this page permits.</summary>
    [Parameter]
    public IReadOnlyList<TreeCommand> Commands { get; set; } = [];

    /// <summary>Raised with the chosen entry.</summary>
    [Parameter]
    public EventCallback<TreeCommand> OnChosen { get; set; }

    /// <summary>Raised when the menu is dismissed without choosing anything.</summary>
    [Parameter]
    public EventCallback OnClosed { get; set; }

    /// <summary>The menu element, which takes focus when it opens.</summary>
    private ElementReference Menu { get; set; }

    /// <summary>The item elements, so the arrow keys can move focus between them.</summary>
    private ElementReference[] Items { get; set; } = [];

    /// <summary>Which item holds the menu's single tab stop.</summary>
    private int _active;

    /// <summary>Whether focus has been moved into the menu for the current opening.</summary>
    private bool _focused;

    /// <summary>Where the menu is drawn.</summary>
    private string Position =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"inset-inline-start: {Math.Round(X)}px; inset-block-start: {Math.Round(Y)}px");

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Items.Length != Commands.Count)
        {
            Items = new ElementReference[Commands.Count];
        }

        if (!IsOpen)
        {
            _focused = false;
            _active = 0;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Focus lands on the first item, which is what makes the menu operable from the key press
        // that opened it — a menu that opens without taking focus is a menu a keyboard user cannot
        // reach at all.
        if (IsOpen && !_focused && Commands.Count > 0)
        {
            _focused = true;

            await FocusAsync(0);
        }
    }

    /// <summary>Moves within the menu, chooses, and dismisses.</summary>
    private async Task OnKeyDownAsync(KeyboardEventArgs args)
    {
        if (Commands.Count == 0) return;

        switch (args.Key)
        {
            case "ArrowDown":
                // Wraps. A menu is a ring, and stopping at the last item means reaching "Delete"
                // from the top takes eight presses instead of one upward.
                await FocusAsync((_active + 1) % Commands.Count);

                break;

            case "ArrowUp":
                await FocusAsync((_active - 1 + Commands.Count) % Commands.Count);

                break;

            case "Home":
                await FocusAsync(0);

                break;

            case "End":
                await FocusAsync(Commands.Count - 1);

                break;

            case "Escape":
            case "Tab":
                await CloseAsync();

                break;

            case "Enter":
            case " ":
                await ChooseAsync(Commands[_active]);

                break;
        }
    }

    /// <summary>Moves the tab stop and the focus to one item.</summary>
    private async Task FocusAsync(int index)
    {
        _active = index;

        StateHasChanged();

        try
        {
            await Items[index].FocusAsync();
        }
        catch (InvalidOperationException)
        {
            // The menu closed between the key press and this call.
        }
    }

    /// <summary>Reports the chosen entry.</summary>
    private Task ChooseAsync(TreeCommand command) => OnChosen.InvokeAsync(command);

    /// <summary>Dismisses the menu.</summary>
    private Task CloseAsync() => OnClosed.InvokeAsync();
}
