using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Common;

/// <summary>
/// CodeMirror 6 as a Blazor component — the Markdown and HTML source surfaces of spec section 14.4
/// (tasks P6-08 and P6-13).
/// </summary>
/// <remarks>
/// One component for both languages, because they differ by one CodeMirror extension and nothing
/// else. Two components would mean two mountings, two teardowns, and two places to forget the CSP
/// nonce.
/// <para>
/// It also carries the scroll reporting split mode needs (P6-10). That lives here rather than in the
/// rich-text editor above it because the thing being measured is CodeMirror's own scroller, which
/// nothing outside this component has a handle on.
/// </para>
/// </remarks>
public partial class SourceEditor : JsEditorComponentBase
{
    private IJSObjectReference? _scrollHandle;

    /// <summary>Either <c>markdown</c> or <c>html</c>; selects the language extension.</summary>
    [Parameter]
    public string Language { get; set; } = "markdown";

    /// <summary>
    /// The editor's accessible name.
    /// </summary>
    /// <remarks>
    /// Set on CodeMirror's own editable element when it mounts, not on the host <c>div</c>. Two
    /// reasons, and both are load-bearing: a bare <c>div</c> may not carry <c>aria-label</c> at all,
    /// and the card's <c>aria-labelledby</c> cannot reach through to a <c>contenteditable</c> the
    /// library creates — the card names the region, and the region is not the control. Without this
    /// a screen reader announces "edit text, blank".
    /// </remarks>
    [Parameter]
    public string Label { get; set; } = "Source";

    /// <summary>Raised with the editor's scroll position as a fraction, when asked for.</summary>
    /// <remarks>
    /// Unset unless split mode is showing, so an editor on its own pays for no scroll interop at all.
    /// </remarks>
    [Parameter]
    public EventCallback<double> OnScrolled { get; set; }

    /// <inheritdoc />
    protected override string ModulePath => "./js/cms-source-editor.js";

    /// <inheritdoc />
    protected override ValueTask MountAsync(
        IJSObjectReference module,
        DotNetObjectReference<JsEditorComponentBase> self) =>
        module.InvokeVoidAsync("create", EditorId, Host, Text, self, Language, Label);

    /// <inheritdoc />
    protected override async ValueTask OnMountedAsync()
    {
        if (!OnScrolled.HasDelegate) return;

        await SubscribeToScrollAsync();
    }

    /// <summary>Replaces the selection, or inserts at the caret when nothing is selected.</summary>
    /// <param name="text">What to insert.</param>
    /// <param name="selectInserted">Whether to leave the inserted text selected.</param>
    /// <remarks>
    /// What CMS-aware insertion writes through (P6-11): the picker decides what the reference is and
    /// the editor only has to put the result where the author was.
    /// </remarks>
    public ValueTask InsertAsync(string text, bool selectInserted = false) =>
        InvokeAsync("replaceSelection", text, selectInserted);

    /// <summary>The text currently selected, which a link picker offers as the link's words.</summary>
    public async ValueTask<string> SelectionAsync() =>
        await InvokeAsync<string>("getSelection") ?? string.Empty;

    /// <summary>Called from JavaScript as the editor is scrolled.</summary>
    /// <param name="fraction">How far down the scrollable height the editor sits, from 0 to 1.</param>
    [JSInvokable]
    public Task OnScrolledFromJs(double fraction) => OnScrolled.InvokeAsync(fraction);

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        if (_scrollHandle is null) return;

        var handle = _scrollHandle;

        _scrollHandle = null;

        // The listener is removed before the editor is destroyed, so a scroll fired by the DOM being
        // torn down cannot call into a component that is halfway through disposing.
        try
        {
            await handle.InvokeVoidAsync("dispose");
            await handle.DisposeAsync();
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // The document is gone; the listener went with it.
        }
    }

    private async ValueTask SubscribeToScrollAsync() =>
        _scrollHandle = await InvokeAsync<IJSObjectReference>("onScroll", SelfReference);
}
