using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Common;

/// <summary>
/// The backoffice's confirmation dialog (tasks P6-03 and P6-21).
/// </summary>
/// <remarks>
/// Bootstrap's modal markup without Bootstrap's JavaScript. The library's own component wants to own
/// showing and hiding, which means the dialog's existence is a fact about the DOM rather than about
/// Blazor's state — and the two then disagree the first time a render happens while it is open.
/// <para>
/// What it does that a <c>&lt;div class="modal"&gt;</c> alone does not: it is announced as a dialog
/// and named by its own heading, it takes focus when it opens and gives it back when it closes,
/// Escape and the backdrop's close button both cancel, and Tab cannot walk out of it into the page
/// behind. The focus half is the part that needs JavaScript, and it is the part most often missing.
/// </para>
/// </remarks>
public partial class ModalDialog : ComponentBase, IAsyncDisposable
{
    /// <summary>Path of the collocated interop module, relative to the host page.</summary>
    private const string ModulePath = "./Components/Admin/Common/ModalDialog.razor.js";

    /// <summary>The browser's JavaScript runtime, used for the focus trap.</summary>
    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>Whether the dialog is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Heading, which is also the dialog's accessible name.</summary>
    [Parameter]
    public string Title { get; set; } = "Confirm";

    /// <summary>What the dialog is asking about.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Label of the button that goes ahead.</summary>
    [Parameter]
    public string ConfirmLabel { get; set; } = "Confirm";

    /// <summary>Label of the button that does not.</summary>
    [Parameter]
    public string CancelLabel { get; set; } = "Cancel";

    /// <summary>Bootstrap variant of the confirm button — danger for anything destructive.</summary>
    [Parameter]
    public string ConfirmClass { get; set; } = "btn-primary";

    /// <summary>Extra class on the dialog, for a wider or narrower one.</summary>
    [Parameter]
    public string? SizeClass { get; set; }

    /// <summary>Whether confirming is currently possible.</summary>
    [Parameter]
    public bool CanConfirm { get; set; } = true;

    /// <summary>Whether the confirmed action is in flight, which disables both the button and Escape.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Raised when the editor goes ahead.</summary>
    [Parameter]
    public EventCallback OnConfirm { get; set; }

    /// <summary>Raised when the editor backs out, by button, by close, or by Escape.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>Distinguishes this dialog's heading id from another's on the same page.</summary>
    private string DialogId { get; } = $"dialog-{Guid.NewGuid():n}";

    /// <summary>The dialog element, which takes focus and holds the trap.</summary>
    private ElementReference Dialog { get; set; }

    /// <summary>The button focus lands on, so Enter confirms without a Tab.</summary>
    private ElementReference ConfirmButton { get; set; }

    /// <summary>The imported interop module.</summary>
    private IJSObjectReference? _module;

    /// <summary>The live focus trap, or null while the dialog is closed.</summary>
    private IJSObjectReference? _trap;

    /// <summary>Whether the trap has been set up for the current opening.</summary>
    private bool _trapped;

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

    /// <summary>Escape backs out, exactly as the cancel button does.</summary>
    /// <remarks>
    /// Ignored while the confirmed action is in flight. Dismissing a dialog whose write has already
    /// started tells the editor it did not happen, which is the one thing a confirmation must never
    /// get wrong.
    /// </remarks>
    private Task OnKeyDownAsync(KeyboardEventArgs args) =>
        args.Key == "Escape" && !IsBusy ? CancelAsync() : Task.CompletedTask;

    /// <summary>Goes ahead.</summary>
    private Task ConfirmAsync() => OnConfirm.InvokeAsync();

    /// <summary>Backs out.</summary>
    private Task CancelAsync() => OnCancel.InvokeAsync();

    /// <summary>Moves focus into the dialog and stops Tab leaving it.</summary>
    private async Task TrapAsync()
    {
        await Quietly(async () =>
        {
            // The confirm button rather than the dialog itself: it is what Enter should press, and
            // landing there means the common case — read it, press Enter — needs no Tab at all.
            await ConfirmButton.FocusAsync();

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
            // Static rendering, or the document is gone. The dialog is still usable either way; it
            // simply does not trap focus in a document that is not being displayed.
        }
    }
}
