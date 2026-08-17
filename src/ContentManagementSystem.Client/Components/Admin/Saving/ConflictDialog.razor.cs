using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Saving;

/// <summary>
/// What a losing save offers: keep mine, take theirs, or look at the difference (task P6-19,
/// spec section 11.8).
/// </summary>
/// <remarks>
/// <strong>No path through this dialog discards work silently.</strong> Keeping yours overwrites
/// theirs, which the server has archived nothing of — so it is offered with what theirs contains one
/// click away. Taking theirs replaces what is on this screen, which exists nowhere else, so it is
/// the one action that asks twice. Closing decides nothing and keeps both.
/// <para>
/// The dialog is handed the winning draft rather than fetching it: it arrived in the body of the
/// <c>409</c> that opened this dialog, and a re-read would be a second race with whoever is still
/// editing. The comparison is fetched, because it is the one thing neither copy contains.
/// </para>
/// </remarks>
public partial class ConflictDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// Path of the focus-trap module.
    /// </summary>
    /// <remarks>
    /// The dialog's own, deliberately shared: a trap is a trap, and a second copy of this one would
    /// be a second place for the Tab-cycle edge cases to be got wrong.
    /// </remarks>
    private const string ModulePath = "./Components/Admin/Common/ModalDialog.razor.js";

    /// <summary>Compares the caller's unsaved payload against the stored draft.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>The browser's JavaScript runtime, used for the focus trap.</summary>
    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>Whether the dialog is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Identity of the contested page.</summary>
    [Parameter]
    public int PageId { get; set; }

    /// <summary>The draft that won the race, as the refusal handed it back.</summary>
    [Parameter]
    public DraftState? Theirs { get; set; }

    /// <summary>The payload this editor is holding and has not managed to save.</summary>
    [Parameter]
    public string? Mine { get; set; }

    /// <summary>Whether the resolution the editor chose is in flight.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Raised to save this editor's version over the stored one.</summary>
    [Parameter]
    public EventCallback OnKeepMine { get; set; }

    /// <summary>Raised to load the stored version into the editor, replacing what is on screen.</summary>
    [Parameter]
    public EventCallback OnTakeTheirs { get; set; }

    /// <summary>Raised when the editor closes without deciding.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>The dialog element, which holds the focus trap.</summary>
    private ElementReference Dialog { get; set; }

    /// <summary>The button focus lands on — the one that keeps this editor's work.</summary>
    private ElementReference KeepMine { get; set; }

    /// <summary>Whether the comparison is showing.</summary>
    private bool ShowDiff { get; set; }

    /// <summary>Whether "use theirs" has been pressed once and is waiting to be confirmed.</summary>
    private bool IsConfirmingTheirs { get; set; }

    /// <summary>The comparison, once it has been fetched.</summary>
    private ContentDiff? Diff { get; set; }

    /// <summary>The imported interop module.</summary>
    private IJSObjectReference? _module;

    /// <summary>The live focus trap, or null while the dialog is closed.</summary>
    private IJSObjectReference? _trap;

    /// <summary>Whether the trap has been set up for the current opening.</summary>
    private bool _trapped;

    /// <summary>What the dialog says happened.</summary>
    private string Explanation => Theirs?.SavedOn is { } savedOn
        ? $"This page was saved by somebody else at {savedOn.ToLocalTime():HH:mm}, after you opened it. " +
          "Saving yours now would replace what they wrote."
        : "This page was saved by somebody else after you opened it. Saving yours now would replace " +
          "what they wrote.";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (IsOpen) return;

        // A closed dialog forgets its half-made decisions. Reopening on the next conflict with
        // "yes — discard mine" already armed would be a destructive button one click from a person
        // who has not read this one yet.
        ShowDiff = false;
        IsConfirmingTheirs = false;
        Diff = null;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !_trapped)
        {
            _trapped = true;

            await TrapAsync();
        }
        else if (!IsOpen && _trapped)
        {
            _trapped = false;

            await ReleaseAsync();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await ReleaseAsync();

        if (_module is not null)
        {
            await Quietly(async () => await _module.DisposeAsync());
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Escape closes without deciding, which is the safe outcome.</summary>
    private Task OnKeyDownAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" && !IsBusy ? CancelAsync() : Task.CompletedTask;

    /// <summary>Shows or hides the comparison, fetching it the first time.</summary>
    private async Task ToggleDiffAsync()
    {
        IsConfirmingTheirs = false;
        ShowDiff = !ShowDiff;

        // Fetched once. Neither side changes while the dialog is open: theirs came with the refusal
        // and mine is on this screen.
        if (ShowDiff && Diff is null)
        {
            Diff = await Client.DiffDraftAsync(PageId, Mine);
        }
    }

    /// <summary>Saves this editor's version over the stored one.</summary>
    private Task KeepMineAsync()
    {
        IsConfirmingTheirs = false;

        return OnKeepMine.InvokeAsync();
    }

    /// <summary>
    /// Loads the stored version, on the second press.
    /// </summary>
    /// <remarks>
    /// The only irreversible action in the dialog, and the only one that asks twice. Everything the
    /// other two buttons overwrite is recoverable — the server keeps versions — while what is on
    /// this screen was never written anywhere.
    /// </remarks>
    private Task TakeTheirsAsync()
    {
        if (!IsConfirmingTheirs)
        {
            IsConfirmingTheirs = true;

            return Task.CompletedTask;
        }

        IsConfirmingTheirs = false;

        return OnTakeTheirs.InvokeAsync();
    }

    /// <summary>Closes without deciding.</summary>
    private Task CancelAsync()
    {
        IsConfirmingTheirs = false;

        return OnCancel.InvokeAsync();
    }

    /// <summary>Moves focus into the dialog and stops Tab leaving it.</summary>
    private async Task TrapAsync()
    {
        await Quietly(async () =>
        {
            // "Keep mine" rather than the first button: it is the one that loses nothing, so it is
            // the one Enter should press for somebody who did not read carefully.
            await KeepMine.FocusAsync();

            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _trap = await _module.InvokeAsync<IJSObjectReference>("trapFocus", Dialog);
        });
    }

    /// <summary>Releases the trap and hands focus back to whatever opened the dialog.</summary>
    private async Task ReleaseAsync()
    {
        if (_trap is null) return;

        var trap = _trap;

        _trap = null;

        await Quietly(async () =>
        {
            await trap.InvokeVoidAsync("dispose");
            await trap.DisposeAsync();
        });
    }

    /// <summary>Runs interop that is allowed to be impossible — a pre-render, or a page going away.</summary>
    private static async Task Quietly(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // Static rendering, or the document is gone. The dialog is still usable either way.
        }
    }
}
