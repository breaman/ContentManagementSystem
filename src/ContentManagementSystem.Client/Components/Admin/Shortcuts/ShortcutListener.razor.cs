using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Shortcuts;

/// <summary>
/// Listens for the backoffice's keyboard shortcuts (task P6-23).
/// </summary>
/// <remarks>
/// Renders nothing and owns no markup: the listener is on the document, because an editor's focus is
/// usually inside something the component tree does not own — a CodeMirror instance, a link in the
/// properties panel — and a shortcut that only worked in one div would be one nobody trusts.
/// <para>
/// <strong>The match happens in .NET, not in the script.</strong> The chord table is
/// <see cref="EditorShortcuts"/>, which is also what the reference dialog renders; a second copy of
/// it in JavaScript would be free to drift, and the failure would be a documented shortcut that does
/// nothing. The script's only judgement is the one .NET cannot make — whether the key landed in a
/// text field.
/// </para>
/// <para>
/// <see cref="IAsyncDisposable"/> for the reason task P6-16 gives: the object reference is what keeps
/// this component alive in the JavaScript registry, and a listener left on the document outlives the
/// page it was for.
/// </para>
/// </remarks>
public partial class ShortcutListener : ComponentBase, IAsyncDisposable
{
    /// <summary>Path of the collocated interop module, relative to the host page.</summary>
    private const string ModulePath = "./Components/Admin/Shortcuts/ShortcutListener.razor.js";

    /// <summary>The browser's JavaScript runtime.</summary>
    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>The chords to listen for.</summary>
    [Parameter]
    public IReadOnlyList<KeyboardShortcut> Shortcuts { get; set; } = [];

    /// <summary>Raised with the id of a shortcut that matched.</summary>
    [Parameter]
    public EventCallback<string> OnShortcut { get; set; }

    /// <summary>The imported interop module.</summary>
    private IJSObjectReference? _module;

    /// <summary>The live listener, or null before it is attached.</summary>
    private IJSObjectReference? _listener;

    /// <summary>This component, as the script calls back into it.</summary>
    private DotNetObjectReference<ShortcutListener>? _self;

    /// <summary>
    /// Answers whether a key press matched a shortcut, and runs it if it did.
    /// </summary>
    /// <param name="key">The <c>KeyboardEvent.key</c> value.</param>
    /// <param name="control">Whether Control or Command was held.</param>
    /// <param name="shift">Whether Shift was held.</param>
    /// <param name="alt">Whether Alt was held.</param>
    /// <returns>Whether the press was claimed, which is what suppresses the browser's own default.</returns>
    /// <remarks>
    /// Alt is matched as "not held" rather than ignored. Alt is the tree's move modifier (task P6-03)
    /// and on several keyboard layouts it composes characters, so a chord that fired with or without
    /// it would be a shortcut that goes off while somebody types an umlaut.
    /// </remarks>
    [JSInvokable]
    public async Task<bool> MatchAsync(string key, bool control, bool shift, bool alt)
    {
        if (alt) return false;

        foreach (var shortcut in Shortcuts)
        {
            if (shortcut.Control == control &&
                shortcut.Shift == shift &&
                string.Equals(shortcut.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                await OnShortcut.InvokeAsync(shortcut.Id);

                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            _self = DotNetObjectReference.Create(this);
            _module = await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _listener = await _module.InvokeAsync<IJSObjectReference>("listen", _self);
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // Static rendering, or the document is going away. Shortcuts are an accelerator for
            // controls that all exist as buttons, so their absence costs nothing an editor cannot do
            // another way — which is also why P6 #4's keyboard operability does not depend on them.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Quietly(async () =>
        {
            if (_listener is not null)
            {
                await _listener.InvokeVoidAsync("dispose");
                await _listener.DisposeAsync();
            }

            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        });

        // Last, and unconditionally: without this the JavaScript registry holds this component for
        // the life of the page, which is exactly the leak task P6-16 exists to prevent.
        _self?.Dispose();

        GC.SuppressFinalize(this);
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
            // Nothing to release, or nothing left to release it in.
        }
    }
}
