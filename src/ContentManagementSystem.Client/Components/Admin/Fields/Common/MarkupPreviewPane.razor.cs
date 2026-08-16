using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Common;

/// <summary>
/// Shows authored source as the published page will show it, and says what publishing will remove
/// (tasks P6-09 and P6-13, spec section 14.4).
/// </summary>
/// <remarks>
/// <strong>Rendered by the server, through the pipeline delivery uses.</strong> Acceptance criterion
/// P6 #2 asks for a preview that matches the published page exactly, and the only way to mean that
/// is to call the same Markdig configuration and the same sanitizer — both of which live in
/// <c>Core</c>, which the browser does not load. So the source goes over the wire and the markup
/// comes back (P6-09).
/// <para>
/// Debounced, because that request is per keystroke otherwise. The delay is long enough that an
/// author typing a sentence causes one render rather than forty, and short enough that pausing to
/// read the preview does not feel like waiting for it.
/// </para>
/// <para>
/// The removals list is what turns the pane into a warning rather than a mirror, and it is the same
/// data the HTML editor's persistent banner is built from (P6-13, acceptance criterion P6 #3).
/// </para>
/// </remarks>
public partial class MarkupPreviewPane : ComponentBase, IAsyncDisposable
{
    /// <summary>How long to wait after the last change before asking the server to render.</summary>
    private static readonly TimeSpan RenderDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>How many removals to list before summarising the rest.</summary>
    private const int RemovalsShown = 8;

    /// <summary>Path of the collocated interop module, relative to the host page.</summary>
    private const string ModulePath =
        "./Components/Admin/Fields/Common/MarkupPreviewPane.razor.js";

    [Inject]
    private IMarkupPreviewClient Client { get; set; } = default!;

    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>The authored source.</summary>
    [Parameter]
    public string? Source { get; set; }

    /// <summary>How the source is written — <c>markdown</c> or <c>html</c>.</summary>
    [Parameter]
    public string Format { get; set; } = MarkupFormats.Markdown;

    /// <summary>Which allowlist to clean the converted markup under.</summary>
    [Parameter]
    public string? Profile { get; set; }

    /// <summary>Element id, so a split view can point at it.</summary>
    [Parameter]
    public string Id { get; set; } = "cms-preview";

    /// <summary>
    /// Where to scroll to, as a fraction of the pane's scrollable height, or null to leave it alone.
    /// </summary>
    /// <remarks>
    /// A fraction rather than a pixel offset (task P6-10). The source and its rendering are
    /// different heights — a one-line image reference becomes a picture — so matching pixels would
    /// drift further apart the further down a long document an author scrolled.
    /// </remarks>
    [Parameter]
    public double? ScrollTo { get; set; }

    /// <summary>The markup the page will show.</summary>
    private MarkupString Html { get; set; }

    /// <summary>What the profile removed, in document order.</summary>
    private IReadOnlyList<SanitizationRemoval> Removals { get; set; } = [];

    /// <summary>Whether a render is in flight.</summary>
    private bool IsRendering { get; set; }

    /// <summary>Whether the last render could not be fetched.</summary>
    private bool Failed { get; set; }

    /// <summary>The scrollable element, so split mode can move it.</summary>
    private ElementReference Body { get; set; }

    /// <summary>Cancels the render a newer change has superseded.</summary>
    private CancellationTokenSource? _render;

    /// <summary>The imported scrolling module, or null until split mode first needs it.</summary>
    private IJSObjectReference? _module;

    /// <summary>The source the last render was made from.</summary>
    private string? _rendered;

    /// <summary>The scroll fraction last pushed, so an unchanged one costs no interop.</summary>
    private double? _scrolled;

    /// <summary>A one-line count of what publishing will take out.</summary>
    private string Summary => Removals.Count == 1
        ? "One thing here will be removed when this is saved:"
        : $"{Removals.Count} things here will be removed when this is saved:";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Source, _rendered, StringComparison.Ordinal)) return;

        await ScheduleAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Scrolling is pushed after the render rather than in <c>OnParametersSet</c>, because the
    /// element being scrolled has to hold the new markup before a fraction of its height means
    /// anything.
    /// </remarks>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (ScrollTo is not { } fraction || fraction == _scrolled) return;

        _scrolled = fraction;

        try
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

            await _module.InvokeVoidAsync("scrollToFraction", Body, fraction);
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // Static rendering, or the pane has gone. Split scrolling is an enhancement; losing it
            // costs an author a scroll, and throwing here would cost them the editor.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _render?.Cancel();
        _render?.Dispose();
        _render = null;

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (Exception exception) when (exception is JSException
                                                  or JSDisconnectedException
                                                  or InvalidOperationException
                                                  or TaskCanceledException)
            {
                // The document is gone; the module went with it.
            }

            _module = null;
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>Waits for the typing to pause, then renders.</summary>
    private async Task ScheduleAsync()
    {
        _render?.Cancel();
        _render?.Dispose();

        var cancellation = _render = new CancellationTokenSource();
        var source = Source;

        IsRendering = true;

        try
        {
            await Task.Delay(RenderDebounce, cancellation.Token);

            var result = await Client.RenderAsync(
                new MarkupPreviewRequest(Format, source, Profile),
                cancellation.Token);

            if (cancellation.IsCancellationRequested) return;

            _rendered = source;
            IsRendering = false;

            if (result is null)
            {
                // The last good markup stays on screen. Blanking the pane on a dropped connection
                // would look like the content had been lost, which is the impression an editor is
                // least able to check.
                Failed = true;

                return;
            }

            Failed = false;
            Html = new MarkupString(result.Html);
            Removals = result.Removals;
        }
        catch (OperationCanceledException)
        {
            // A newer change owns the pane now; it will set the state when it lands.
        }
    }
}
