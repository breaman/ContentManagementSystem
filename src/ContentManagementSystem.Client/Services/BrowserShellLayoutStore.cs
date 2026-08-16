using ContentManagementSystem.Client.Components.Admin.Shell;

using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Services;

/// <summary>
/// Keeps the shell's pane geometry in the browser's <c>localStorage</c> (task P6-01).
/// </summary>
/// <remarks>
/// Registered on the server as well as in the browser, because the shell pre-renders. Every call
/// there fails — static rendering has no JavaScript to talk to — and every failure is swallowed, so
/// the pre-rendered shell shows the default layout and the stored one arrives when WebAssembly
/// takes over. That flash is the price of pre-rendering the shell at all, and it is a better trade
/// than an editor watching a spinner where the tree should be.
/// </remarks>
/// <param name="js">The browser's JavaScript runtime.</param>
public sealed class BrowserShellLayoutStore(IJSRuntime js) : IShellLayoutStore, IAsyncDisposable
{
    /// <summary>Path of the shell's collocated interop module, relative to the host page.</summary>
    private const string ModulePath = "./Components/Admin/Shell/AdminShell.razor.js";

    /// <summary>The imported module, or null before the first successful import.</summary>
    private IJSObjectReference? _module;

    /// <summary>Whether an import has already failed, so later calls do not retry it every time.</summary>
    private bool _unavailable;

    /// <inheritdoc />
    public async ValueTask<ShellLayout> LoadAsync(
        string userKey,
        CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken);

        if (module is null)
        {
            return ShellLayout.Default;
        }

        try
        {
            var stored = await module.InvokeAsync<ShellLayout?>(
                "loadLayout",
                cancellationToken,
                userKey);

            return stored?.Sanitized() ?? ShellLayout.Default;
        }
        catch (JSException)
        {
            // Stored JSON that no longer deserializes — an older shape, or something else that wrote
            // to the key. The default layout is the right answer, and clearing the entry is not
            // necessary because the next save overwrites it.
            return ShellLayout.Default;
        }
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(
        string userKey,
        ShellLayout layout,
        CancellationToken cancellationToken = default)
    {
        var module = await ModuleAsync(cancellationToken);

        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("saveLayout", cancellationToken, userKey, layout);
        }
        catch (JSException)
        {
            // The module already swallows storage failures; this catches the transport itself going
            // away mid-call, which is a navigation, not a fault.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page is gone, and with it everything this reference pointed at.
        }
    }

    /// <summary>Imports the interop module once, or answers null where there is no JavaScript.</summary>
    private async ValueTask<IJSObjectReference?> ModuleAsync(CancellationToken cancellationToken)
    {
        if (_module is not null || _unavailable)
        {
            return _module;
        }

        try
        {
            _module = await js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath);
        }
        catch (Exception exception) when (exception is JSException
                                              or InvalidOperationException
                                              or JSDisconnectedException
                                              or TaskCanceledException)
        {
            // InvalidOperationException is the pre-render case: the framework refuses interop during
            // static rendering rather than returning a runtime that does nothing.
            _unavailable = true;
        }

        return _module;
    }
}
