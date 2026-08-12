using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace S3.EditorInterop.Client.Components;

/// <summary>
/// Shared interop plumbing for a Blazor component wrapping a JavaScript editor: import the module
/// once, hand the editor a <see cref="DotNetObjectReference{TValue}"/> for change notifications, push
/// programmatic writes back down, and tear all of it down on disposal.
/// </summary>
public abstract class JsEditorComponentBase : ComponentBase, IAsyncDisposable
{
    private DotNetObjectReference<JsEditorComponentBase>? _selfReference;
    private IJSObjectReference? _module;
    private string _lastSynchronizedValue = string.Empty;
    private bool _initialized;

    [Inject]
    protected IJSRuntime Js { get; set; } = default!;

    [Parameter]
    public string Value { get; set; } = string.Empty;

    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The element the editor mounts into.</summary>
    protected ElementReference Host { get; set; }

    /// <summary>Stable id used as the key of the editor's entry in the JS-side registry.</summary>
    protected string ElementId { get; } = $"editor-{Guid.NewGuid():n}";

    /// <summary>Name of the exported factory function in <c>editors.js</c>.</summary>
    protected abstract string CreateFunction { get; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // A local ESM module, imported by relative path. Nothing is fetched from a CDN, which is
            // what lets the CSP stay strict (spec §20.5).
            _module = await Js.InvokeAsync<IJSObjectReference>("import", "./js/editors.js");
            _selfReference = DotNetObjectReference.Create(this);
            _lastSynchronizedValue = Value;

            await _module.InvokeVoidAsync(CreateFunction, ElementId, Host, Value, _selfReference);

            _initialized = true;

            return;
        }

        // Push a programmatic (.NET-side) change down into the editor. Comparing against the last
        // synchronized value is what stops the two directions echoing each other forever.
        if (_initialized && _module is not null &&
            !string.Equals(Value, _lastSynchronizedValue, StringComparison.Ordinal))
        {
            _lastSynchronizedValue = Value;

            await _module.InvokeVoidAsync("setValue", ElementId, Value);
        }
    }

    /// <summary>Called from JavaScript whenever the editor's document changes.</summary>
    /// <param name="value">The editor's current content.</param>
    [JSInvokable]
    public async Task OnValueChangedFromJs(string value)
    {
        _lastSynchronizedValue = value;
        Value = value;

        await ValueChanged.InvokeAsync(value);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.InvokeVoidAsync("dispose", ElementId);
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The circuit is gone; there is nothing left to tear down on the other side.
            }
        }

        // Without this the component is kept alive by the JS-side reference for the lifetime of the
        // page — the leak the spike is looking for.
        _selfReference?.Dispose();

        GC.SuppressFinalize(this);
    }
}
