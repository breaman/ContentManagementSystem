using System.Globalization;
using System.Security.Claims;

using ContentManagementSystem.Client.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Shell;

/// <summary>
/// The three-pane backoffice shell: content tree, editing canvas, properties (task P6-01).
/// </summary>
/// <remarks>
/// The frame of spec section 14.1 and nothing else. It owns the geometry — which pane is how wide,
/// which are collapsed, and where that is remembered — and takes the three panes' contents as render
/// fragments, so the tree, the canvas, and the properties panel can each be built, tested, and
/// replaced without any of them knowing the others exist.
/// <para>
/// <strong>Resizing runs in JavaScript, deliberately.</strong> A pointer drag fires
/// <c>pointermove</c> far faster than a WebAssembly round trip and a re-render per event can keep
/// up with, so the collocated module writes the width straight onto a CSS custom property during the
/// drag and reports the final value here once, on release. Keyboard resizing stays in .NET, where a
/// key press per step costs nothing — and it is the path that has to work, since a separator that
/// can only be dragged is a pane a keyboard user cannot resize at all (spec section 28).
/// </para>
/// <para>
/// Below the tablet breakpoint the three panes stack instead of sitting side by side, and the
/// separators disappear. Stacking rather than turning the side panes into overlays keeps every pane
/// in the document in reading order, which is what a screen reader and a keyboard both follow.
/// </para>
/// </remarks>
public partial class AdminShell : ComponentBase, IAsyncDisposable
{
    /// <summary>
    /// Path of the interop module, relative to the host page.
    /// </summary>
    /// <remarks>
    /// A collocated <c>.razor.js</c> file rather than something under <c>wwwroot</c>: this
    /// repository's <c>wwwroot</c> is build output — Sass writes into it and npm copies Bootstrap
    /// into it — and is not in source control, so a module left there would work on the machine
    /// that wrote it and be missing from every clone. Collocation also keeps the module beside the
    /// only component that imports it.
    /// </remarks>
    private const string ModulePath = "./Components/Admin/Shell/AdminShell.razor.js";

    /// <summary>CSS custom property holding the tree column's width.</summary>
    private const string TreeVariable = "--cms-shell-tree-col";

    /// <summary>CSS custom property holding the properties column's width.</summary>
    private const string PropertiesVariable = "--cms-shell-properties-col";

    /// <summary>
    /// How long a geometry change settles before it is written to storage.
    /// </summary>
    /// <remarks>
    /// Held down, an arrow key repeats around thirty times a second, and each repeat is a step. This
    /// turns one held key into one write instead of thirty.
    /// </remarks>
    private static readonly TimeSpan PersistDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Remembers the geometry between visits.</summary>
    [Inject]
    private IShellLayoutStore LayoutStore { get; set; } = default!;

    /// <summary>The browser's JavaScript runtime, used to attach the drag handlers.</summary>
    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>Who is signed in, so one machine's editors do not share a layout.</summary>
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    /// <summary>Contents of the left pane — in practice the content tree.</summary>
    [Parameter]
    public RenderFragment? Navigation { get; set; }

    /// <summary>Contents of the centre pane, where the editing happens.</summary>
    [Parameter]
    public RenderFragment? Canvas { get; set; }

    /// <summary>Contents of the right pane — page metadata, SEO, publishing.</summary>
    [Parameter]
    public RenderFragment? Properties { get; set; }

    /// <summary>The bar across the bottom: save state, who else is editing, the publish controls.</summary>
    [Parameter]
    public RenderFragment? StatusBar { get; set; }

    /// <summary>Accessible name of the left pane.</summary>
    [Parameter]
    public string TreeLabel { get; set; } = "Content tree";

    /// <summary>Accessible name of the centre pane.</summary>
    [Parameter]
    public string CanvasLabel { get; set; } = "Editing canvas";

    /// <summary>Accessible name of the right pane.</summary>
    [Parameter]
    public string PropertiesLabel { get; set; } = "Properties";

    /// <summary>
    /// Distinguishes this shell's stored geometry from another's.
    /// </summary>
    /// <remarks>
    /// Screens that are shells in their own right — the media library, say — want their own pane
    /// widths rather than inheriting the page editor's.
    /// </remarks>
    [Parameter]
    public string LayoutKey { get; set; } = "pages";

    /// <summary>The geometry currently in force.</summary>
    private ShellLayout Layout { get; set; } = ShellLayout.Default;

    /// <summary>The grid element the CSS custom properties are set on.</summary>
    private ElementReference Host { get; set; }

    /// <summary>The separator between the tree and the canvas.</summary>
    private ElementReference TreeHandle { get; set; }

    /// <summary>The separator between the canvas and the properties panel.</summary>
    private ElementReference PropertiesHandle { get; set; }

    /// <summary>The imported interop module, or null where there is no JavaScript.</summary>
    private IJSObjectReference? _module;

    /// <summary>What the two resizers hand back so their listeners can be removed again.</summary>
    private IJSObjectReference? _treeResizer;

    /// <summary>What the two resizers hand back so their listeners can be removed again.</summary>
    private IJSObjectReference? _propertiesResizer;

    /// <summary>The reference JavaScript calls back through.</summary>
    private DotNetObjectReference<AdminShell>? _self;

    /// <summary>Storage key for the signed-in editor.</summary>
    private string _userKey = "anonymous";

    /// <summary>Cancels a pending write when another change arrives before it fires.</summary>
    private CancellationTokenSource? _persist;

    /// <summary>The inline style that carries both column widths.</summary>
    /// <remarks>
    /// A collapsed pane's column is <c>auto</c> rather than its stored width, so the pane shrinks to
    /// the rail its toggle button sits in. The width itself is kept, not zeroed — expanding gives
    /// back the pane the editor had, not the default one.
    /// </remarks>
    private string GeometryStyle =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{TreeVariable}: {Column(Layout.TreeWidth, Layout.TreeCollapsed)}; " +
            $"{PropertiesVariable}: {Column(Layout.PropertiesWidth, Layout.PropertiesCollapsed)}");

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _userKey = await ResolveUserKeyAsync();

        var stored = await LayoutStore.LoadAsync(_userKey, CancellationToken.None);

        // Only re-render when the stored layout differs from what was pre-rendered. Records compare
        // by value, so an editor who has never resized anything sees no second render at all.
        if (stored != Layout)
        {
            Layout = stored;

            StateHasChanged();
        }

        await AttachResizersAsync();
    }

    /// <summary>Reports the width a pointer drag of the tree separator settled on.</summary>
    /// <param name="width">The pane's new width, in CSS pixels.</param>
    [JSInvokable]
    public Task OnTreeWidthChanged(double width) =>
        ApplyAsync(Layout with { TreeWidth = width });

    /// <summary>Reports the width a pointer drag of the properties separator settled on.</summary>
    /// <param name="width">The pane's new width, in CSS pixels.</param>
    [JSInvokable]
    public Task OnPropertiesWidthChanged(double width) =>
        ApplyAsync(Layout with { PropertiesWidth = width });

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Ordered innermost first: the resizers hold listeners on elements this component owns, and
        // leaving one attached is exactly the leak P6-16 exists to prevent.
        await DisposeQuietlyAsync(_treeResizer, "dispose");
        await DisposeQuietlyAsync(_propertiesResizer, "dispose");
        await DisposeQuietlyAsync(_module, method: null);

        _self?.Dispose();

        _persist?.Cancel();
        _persist?.Dispose();

        GC.SuppressFinalize(this);
    }

    /// <summary>Renders a boolean as an ARIA attribute value.</summary>
    /// <remarks>
    /// <c>ToString()</c> would render <c>True</c>, which is not one of the two values
    /// <c>aria-expanded</c> accepts, and a value it does not accept is read as unsupported.
    /// </remarks>
    private static string Aria(bool value) => value ? "true" : "false";

    /// <summary>Renders a pane width for an ARIA range attribute.</summary>
    private static string Width(double value) =>
        Math.Round(value).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Renders one grid column: a fixed width, or <c>auto</c> when the pane is collapsed.</summary>
    private static string Column(double width, bool collapsed) =>
        collapsed
            ? "auto"
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Round(width)}px");

    /// <summary>Collapses or expands the content tree.</summary>
    private Task ToggleTreeAsync() =>
        ApplyAsync(Layout with { TreeCollapsed = !Layout.TreeCollapsed });

    /// <summary>Collapses or expands the properties panel.</summary>
    private Task TogglePropertiesAsync() =>
        ApplyAsync(Layout with { PropertiesCollapsed = !Layout.PropertiesCollapsed });

    /// <summary>Resizes the tree pane from the keyboard.</summary>
    private Task OnTreeHandleKeyDownAsync(KeyboardEventArgs args)
    {
        var width = Resize(Layout.TreeWidth, args, widenKey: "ArrowRight");

        return width is null
            ? Task.CompletedTask
            : ApplyAsync(Layout with { TreeWidth = width.Value });
    }

    /// <summary>Resizes the properties pane from the keyboard.</summary>
    /// <remarks>
    /// The pane is on the right, so <em>left</em> widens it. Mirroring the keys rather than sharing
    /// one direction is what makes the arrow key mean "move this separator that way" on both sides,
    /// which is the only mapping a person can predict without looking it up.
    /// </remarks>
    private Task OnPropertiesHandleKeyDownAsync(KeyboardEventArgs args)
    {
        var width = Resize(Layout.PropertiesWidth, args, widenKey: "ArrowLeft");

        return width is null
            ? Task.CompletedTask
            : ApplyAsync(Layout with { PropertiesWidth = width.Value });
    }

    /// <summary>
    /// Works out the width a key press asks for.
    /// </summary>
    /// <param name="current">The pane's width now.</param>
    /// <param name="args">The key press.</param>
    /// <param name="widenKey">Which arrow key makes this pane wider.</param>
    /// <returns>The requested width, or null when the key means nothing here.</returns>
    private static double? Resize(double current, KeyboardEventArgs args, string widenKey)
    {
        var step = args.ShiftKey ? ShellLayout.CoarseKeyboardStep : ShellLayout.KeyboardStep;
        var narrowKey = widenKey == "ArrowRight" ? "ArrowLeft" : "ArrowRight";

        var requested = args.Key switch
        {
            var key when key == widenKey => current + step,
            var key when key == narrowKey => current - step,
            "Home" => ShellLayout.MinPaneWidth,
            "End" => ShellLayout.MaxPaneWidth,
            _ => (double?)null,
        };

        if (requested is null)
        {
            return null;
        }

        var clamped = Math.Clamp(requested.Value, ShellLayout.MinPaneWidth, ShellLayout.MaxPaneWidth);

        // Already against the stop: report nothing so the shell does not re-render and rewrite
        // storage on every further press in that direction.
        return Math.Abs(clamped - current) < 0.5 ? null : clamped;
    }

    /// <summary>Adopts a new geometry, re-renders, and schedules it to be remembered.</summary>
    private Task ApplyAsync(ShellLayout layout)
    {
        Layout = layout.Sanitized();

        SchedulePersist();
        StateHasChanged();

        return Task.CompletedTask;
    }

    /// <summary>Writes the layout once the changes stop arriving.</summary>
    private void SchedulePersist()
    {
        _persist?.Cancel();
        _persist?.Dispose();

        var cancellation = new CancellationTokenSource();

        _persist = cancellation;

        _ = PersistAsync(cancellation.Token);
    }

    /// <summary>The debounced half of <see cref="SchedulePersist"/>.</summary>
    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(PersistDelay, cancellationToken);

            await LayoutStore.SaveAsync(_userKey, Layout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later change, or the shell went away. Either way the write that
            // matters is the one that is still pending.
        }
    }

    /// <summary>Names the storage entry after the signed-in editor and this shell.</summary>
    private async Task<string> ResolveUserKeyAsync()
    {
        var identifier = AuthenticationState is null
            ? null
            : (await AuthenticationState).User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return $"{LayoutKey}.{identifier ?? "anonymous"}";
    }

    /// <summary>Hands both separators to the JavaScript that makes them draggable.</summary>
    private async Task AttachResizersAsync()
    {
        try
        {
            _module = await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _self = DotNetObjectReference.Create(this);

            _treeResizer = await _module.InvokeAsync<IJSObjectReference>(
                "attachResizer",
                TreeHandle,
                Host,
                new ResizerOptions(TreeVariable, Sign: 1, nameof(OnTreeWidthChanged)),
                _self);

            _propertiesResizer = await _module.InvokeAsync<IJSObjectReference>(
                "attachResizer",
                PropertiesHandle,
                Host,
                new ResizerOptions(PropertiesVariable, Sign: -1, nameof(OnPropertiesWidthChanged)),
                _self);
        }
        catch (Exception exception) when (exception is JSException
                                              or InvalidOperationException
                                              or JSDisconnectedException
                                              or TaskCanceledException)
        {
            // No JavaScript — a static pre-render, or a browser that refused the module. The panes
            // are still collapsible and still resizable from the keyboard, which is the path that
            // has to work regardless.
        }
    }

    /// <summary>Tears down one interop object, tolerating a page that has already gone.</summary>
    private static async ValueTask DisposeQuietlyAsync(IJSObjectReference? reference, string? method)
    {
        if (reference is null)
        {
            return;
        }

        try
        {
            if (method is not null)
            {
                await reference.InvokeVoidAsync(method);
            }

            await reference.DisposeAsync();
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // The document is gone, and every listener with it.
        }
    }

    /// <summary>What <c>attachResizer</c> needs to know about one separator.</summary>
    /// <param name="Variable">CSS custom property the drag writes.</param>
    /// <param name="Sign">+1 when dragging right widens the pane, -1 when it narrows it.</param>
    /// <param name="Method">The <c>[JSInvokable]</c> method called once, on release.</param>
    private sealed record ResizerOptions(string Variable, int Sign, string Method)
    {
        /// <summary>Narrowest the drag may go.</summary>
        public double Min => ShellLayout.MinPaneWidth;

        /// <summary>Widest the drag may go.</summary>
        public double Max => ShellLayout.MaxPaneWidth;
    }
}
