using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Common;

/// <summary>
/// The interop plumbing every JavaScript-backed editor needs, in one place
/// (tasks P6-08 and P6-16, spike S3).
/// </summary>
/// <remarks>
/// Import the module once, hand the editor a <see cref="DotNetObjectReference{TValue}"/> so it can
/// report changes, push programmatic writes back down without echoing, and tear all of it down when
/// Blazor removes the component from the render tree.
/// <para>
/// <strong>A base class rather than a pattern to follow.</strong> Spike S3 found three things that
/// have to be right for disposal to be clean, and only the first is obvious: the editor's own
/// teardown, Quill's toolbar (which it appends as a <em>sibling</em> and never removes), and
/// <c>DotNetObjectReference.Dispose()</c>, without which the JS registry keeps the component alive
/// for the lifetime of the page. Two of those are handled here so no individual wrapper can forget
/// them; the third is the concrete editor's <c>destroy()</c> on the JavaScript side.
/// </para>
/// <para>
/// <strong>Echo suppression is on both sides.</strong> This class compares against the last
/// synchronized value before pushing a write down, and the JavaScript registry compares again before
/// applying it. Either guard alone leaves a window in which each side's update re-triggers the
/// other's — which surfaces as a cursor jumping to position 0 while somebody is typing.
/// </para>
/// </remarks>
public abstract class JsEditorComponentBase : ComponentBase, IAsyncDisposable
{
    private DotNetObjectReference<JsEditorComponentBase>? _self;

    private IJSObjectReference? _module;

    private string _synchronized = string.Empty;

    private bool _mounted;

    /// <summary>The browser's JavaScript runtime.</summary>
    [Inject]
    protected IJSRuntime Js { get; set; } = default!;

    /// <summary>The document the editor holds.</summary>
    [Parameter]
    public string Text { get; set; } = string.Empty;

    /// <summary>Raised with the editor's content whenever it changes.</summary>
    [Parameter]
    public EventCallback<string> TextChanged { get; set; }

    /// <summary>Whether the surface refuses edits.</summary>
    [Parameter]
    public bool ReadOnly { get; set; }

    /// <summary>Path of the bundled module this editor mounts from.</summary>
    /// <remarks>
    /// A local static asset under <c>wwwroot/js</c>, built by esbuild as part of the project's build
    /// (ADR-0013). Nothing is fetched from another origin, which is what lets the content security
    /// policy stay strict.
    /// </remarks>
    protected abstract string ModulePath { get; }

    /// <summary>The element the editor mounts into.</summary>
    protected ElementReference Host { get; set; }

    /// <summary>
    /// Stable id keying this editor's entry in the JavaScript registry.
    /// </summary>
    /// <remarks>
    /// Generated per component instance rather than derived from the slot key, because a block list
    /// can hold two blocks with the same property key and the registry must be able to tell their
    /// editors apart.
    /// </remarks>
    protected string EditorId { get; } = $"cms-editor-{Guid.NewGuid():n}";

    /// <summary>Whether the editor has been created on the JavaScript side.</summary>
    protected bool IsMounted => _mounted;

    /// <summary>
    /// The reference JavaScript calls this component back through, or null before it is mounted.
    /// </summary>
    /// <remarks>
    /// Exposed so a subclass subscribing to something else on the JavaScript side — split mode's
    /// scroll reporting, for one — passes <em>this</em> reference rather than creating a second one.
    /// A second reference is a second thing to dispose, and the one that gets forgotten is the one
    /// nothing in the disposal chain knows about.
    /// </remarks>
    protected DotNetObjectReference<JsEditorComponentBase>? SelfReference => _self;

    /// <summary>Creates the editor once the host element exists.</summary>
    /// <param name="module">The imported module.</param>
    /// <param name="self">The reference the editor reports changes through.</param>
    /// <remarks>
    /// Each editor passes its own extra arguments — a language for the source editor, a read-only
    /// flag for the WYSIWYG one — which is the only thing the two mountings differ by.
    /// </remarks>
    protected abstract ValueTask MountAsync(
        IJSObjectReference module,
        DotNetObjectReference<JsEditorComponentBase> self);

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _self = DotNetObjectReference.Create(this);
            _synchronized = Text;

            await MountAsync(_module, _self);

            _mounted = true;

            await OnMountedAsync();

            return;
        }

        if (!_mounted || _module is null) return;

        if (!string.Equals(Text, _synchronized, StringComparison.Ordinal))
        {
            _synchronized = Text;

            await _module.InvokeVoidAsync("setValue", EditorId, Text);
        }
    }

    /// <summary>Runs once the editor exists, for anything that has to be attached to it.</summary>
    protected virtual ValueTask OnMountedAsync() => ValueTask.CompletedTask;

    /// <summary>Called from JavaScript whenever the editor's document changes.</summary>
    /// <param name="value">The editor's current content.</param>
    [JSInvokable]
    public async Task OnValueChangedFromJs(string value)
    {
        _synchronized = value;
        Text = value;

        await TextChanged.InvokeAsync(value);
    }

    /// <summary>Calls into this editor's module, or does nothing once it is gone.</summary>
    /// <param name="function">Name of the exported function.</param>
    /// <param name="arguments">Its arguments after the editor id, which is always first.</param>
    protected async ValueTask InvokeAsync(string function, params object?[] arguments)
    {
        if (!_mounted || _module is null) return;

        await Quietly(async () =>
            await _module.InvokeVoidAsync(function, [EditorId, .. arguments]));
    }

    /// <summary>Calls into this editor's module for a value.</summary>
    /// <typeparam name="T">What the function returns.</typeparam>
    /// <param name="function">Name of the exported function.</param>
    /// <param name="arguments">Its arguments after the editor id, which is always first.</param>
    /// <returns>What the function returned, or the default once the editor is gone.</returns>
    protected async ValueTask<T?> InvokeAsync<T>(string function, params object?[] arguments)
    {
        if (!_mounted || _module is null) return default;

        try
        {
            return await _module.InvokeAsync<T>(function, [EditorId, .. arguments]);
        }
        catch (Exception exception) when (IsTeardown(exception))
        {
            return default;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The whole of R14's mitigation, and the reason it is on the base class: an editor's DOM, its
    /// listeners, and the .NET reference the JavaScript side holds all have to go, and any one of
    /// them left behind keeps the other two alive.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await DisposeCoreAsync();

        if (_module is not null)
        {
            await Quietly(async () =>
            {
                await _module.InvokeVoidAsync("dispose", EditorId);
                await _module.DisposeAsync();
            });
        }

        _module = null;
        _mounted = false;

        // Without this the component is kept alive by the JavaScript-side reference for the lifetime
        // of the page, whatever else was torn down.
        _self?.Dispose();
        _self = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>Releases anything a concrete editor attached, before the editor itself goes.</summary>
    protected virtual ValueTask DisposeCoreAsync() => ValueTask.CompletedTask;

    /// <summary>Runs interop that is allowed to be impossible — a pre-render, or a page going away.</summary>
    private static async ValueTask Quietly(Func<ValueTask> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception) when (IsTeardown(exception))
        {
            // Static rendering, or the document is gone. There is nothing left to tear down on the
            // other side, and throwing here would take the whole disposal chain with it.
        }
    }

    private static bool IsTeardown(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;
}
