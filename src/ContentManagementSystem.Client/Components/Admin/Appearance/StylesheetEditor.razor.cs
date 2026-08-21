using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Appearance;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Appearance;

/// <summary>
/// The site stylesheet editor (task P10-11, spec section 30.3).
/// </summary>
/// <remarks>
/// Three panes and one promise: what is typed here changes nothing until it is published, and the
/// preview beside it is a real page of the site rather than a swatch board — a stylesheet is judged
/// against the pages it will be seen on.
/// <para>
/// <strong>The diagnostics come from the server.</strong> The same validator runs on save and on
/// publish, so a stylesheet that shows no problems here cannot be refused a moment later by a second
/// implementation that disagreed. It is debounced rather than run per keystroke, because the answer
/// is only interesting once somebody has stopped typing.
/// </para>
/// </remarks>
public partial class StylesheetEditor : ComponentBase, IDisposable
{
    /// <summary>
    /// How long the typing has to stop before the stylesheet is sent for checking.
    /// </summary>
    /// <remarks>
    /// Long enough that writing a rule does not produce a request per character, short enough that
    /// an administrator who has stopped to look at the screen has an answer by the time they do.
    /// </remarks>
    private static readonly TimeSpan DiagnosticsDelay = TimeSpan.FromMilliseconds(600);

    private CancellationTokenSource? diagnostics;

    /// <summary>Reads and writes the stylesheet.</summary>
    [Inject]
    public ISiteStylesheetClient Stylesheet { get; set; } = default!;

    /// <summary>Finds a page to preview against.</summary>
    [Inject]
    public IPageClient Pages { get; set; } = default!;

    /// <summary>Reports what a refused write said.</summary>
    [Inject]
    public IToastService Toasts { get; set; } = default!;

    /// <summary>The stored stylesheet, or null while it is loading.</summary>
    protected SiteStylesheetDetail? Sheet { get; private set; }

    /// <summary>What is in the editor, which may differ from what is stored.</summary>
    protected string Draft { get; private set; } = string.Empty;

    /// <summary>What the validator makes of <see cref="Draft"/>.</summary>
    protected IReadOnlyList<CssDiagnostic> Diagnostics { get; private set; } = [];

    /// <summary>The published history, newest first.</summary>
    protected IReadOnlyList<SiteStylesheetRevisionSummary> Revisions { get; private set; } = [];

    /// <summary>Pages the preview pane can render.</summary>
    protected IReadOnlyList<PageSummary> PreviewPages { get; private set; } = [];

    /// <summary>Which page the preview is showing.</summary>
    protected int? PreviewPageId { get; private set; }

    /// <summary>
    /// Changed to force the preview frame to reload.
    /// </summary>
    /// <remarks>
    /// An iframe whose <c>src</c> has not changed is not re-fetched, so saving the draft would leave
    /// the pane showing the previous one — which is the single most confusing thing this screen
    /// could do.
    /// </remarks>
    protected int PreviewNonce { get; private set; }

    /// <summary>What the publish dialog's note box holds.</summary>
    protected string? PublishNote { get; set; }

    /// <summary>Whether the publish dialog is open.</summary>
    protected bool IsPublishOpen { get; private set; }

    /// <summary>Whether a write is in flight, which disables the buttons that would start another.</summary>
    protected bool IsBusy { get; private set; }

    /// <summary>Errors from the last refused write.</summary>
    protected IReadOnlyList<ApiDiagnostic> Errors { get; private set; } = [];

    /// <summary>Whether the draft differs from what is stored.</summary>
    protected bool CanSave =>
        Sheet is not null && !string.Equals(Draft, Sheet.DraftCss, StringComparison.Ordinal);

    /// <summary>
    /// Whether there is something to publish.
    /// </summary>
    /// <remarks>
    /// Publishing takes what is <em>stored</em>, so an unsaved edit is not publishable — and saying
    /// so on the button is better than publishing the previous draft and leaving somebody to work
    /// out why their change is not live.
    /// </remarks>
    protected bool CanPublish =>
        Sheet is { HasUnpublishedChanges: true } && !CanSave && Diagnostics.Count == 0;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await ReloadAsync();
        await LoadPreviewPagesAsync();
    }

    /// <summary>Reloads the stylesheet and its history, discarding anything unsaved.</summary>
    protected async Task ReloadAsync()
    {
        Sheet = await Stylesheet.GetAsync();
        Draft = Sheet?.DraftCss ?? string.Empty;
        Diagnostics = Sheet?.Diagnostics ?? [];
        Revisions = await Stylesheet.GetRevisionsAsync();
        Errors = [];
    }

    /// <summary>Records a change in the editor and schedules a check of it.</summary>
    /// <param name="css">What the editor now holds.</param>
    protected async Task OnDraftChangedAsync(string css)
    {
        Draft = css;

        await ScheduleDiagnosticsAsync();
    }

    /// <summary>Saves the draft.</summary>
    protected async Task SaveAsync()
    {
        if (IsBusy || Sheet is null) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Stylesheet.SaveDraftAsync(Draft, Sheet.RowVersion);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Sheet = result.Value;
            Draft = Sheet!.DraftCss;
            Diagnostics = Sheet.Diagnostics;
            RefreshPreview();

            Toasts.ShowSuccess("Draft saved. Visitors are still seeing the published stylesheet.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Opens the publish dialog.</summary>
    protected Task ShowPublishAsync()
    {
        IsPublishOpen = true;
        PublishNote = null;

        return Task.CompletedTask;
    }

    /// <summary>Closes the publish dialog without publishing.</summary>
    protected void ClosePublish() => IsPublishOpen = false;

    /// <summary>Publishes the stored draft.</summary>
    protected async Task PublishAsync()
    {
        if (IsBusy || Sheet is null) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Stylesheet.PublishAsync(PublishNote);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            IsPublishOpen = false;
            Sheet = result.Value;
            Revisions = await Stylesheet.GetRevisionsAsync();

            Toasts.ShowSuccess("Published. Every visitor sees it on their next request.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Publishes an earlier revision, or nothing at all.</summary>
    /// <param name="revisionId">The revision, or null to publish nothing.</param>
    protected async Task RevertAsync(int? revisionId)
    {
        if (IsBusy) return;

        IsBusy = true;
        Errors = [];

        try
        {
            var result = await Stylesheet.RevertAsync(revisionId, copyToDraft: false);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Sheet = result.Value;
            Revisions = await Stylesheet.GetRevisionsAsync();

            Toasts.ShowSuccess(revisionId is null
                ? "The stylesheet is unpublished. The site is using the design it shipped with."
                : "That revision is live. Your draft is untouched.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Loads a revision's CSS into the editor without publishing anything.</summary>
    /// <param name="revision">The revision to load.</param>
    protected async Task LoadIntoDraftAsync(SiteStylesheetRevisionSummary revision)
    {
        ArgumentNullException.ThrowIfNull(revision);

        if (IsBusy) return;

        var css = await Stylesheet.GetRevisionCssAsync(revision.Id);

        if (css is null)
        {
            Toasts.ShowError("That revision could not be read.");

            return;
        }

        Draft = css;

        await ScheduleDiagnosticsAsync();
    }

    /// <summary>Reloads the preview frame.</summary>
    protected void RefreshPreview() => PreviewNonce++;

    /// <summary>Switches the preview to another page.</summary>
    /// <param name="args">The select's new value.</param>
    protected void OnPreviewPageChanged(ChangeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (int.TryParse(args.Value?.ToString(), CultureInfo.InvariantCulture, out var id))
        {
            PreviewPageId = id;
            RefreshPreview();
        }
    }

    /// <summary>Formats an instant for the screen, or an em dash when there is none.</summary>
    /// <param name="instant">The instant.</param>
    protected static string FormatWhen(DateTimeOffset? instant) =>
        instant is null ? "—" : instant.Value.ToLocalTime().ToString("f", CultureInfo.CurrentCulture);

    /// <summary>Formats "by somebody", or nothing at all when nobody is recorded.</summary>
    /// <param name="who">The publisher's display name.</param>
    protected static string FormatWho(string? who) =>
        string.IsNullOrEmpty(who) ? string.Empty : $" by {who}";

    /// <summary>Formats a byte delta with its sign, which is the part that carries the meaning.</summary>
    /// <param name="delta">The difference in bytes.</param>
    protected static string FormatDelta(int delta) =>
        delta switch
        {
            > 0 => string.Create(CultureInfo.CurrentCulture, $"+{delta:N0}"),
            < 0 => string.Create(CultureInfo.CurrentCulture, $"{delta:N0}"),
            _ => "no change",
        };

    /// <inheritdoc />
    public void Dispose()
    {
        diagnostics?.Cancel();
        diagnostics?.Dispose();
        diagnostics = null;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sends the draft for checking once the typing has stopped.
    /// </summary>
    /// <remarks>
    /// The previous wait is cancelled rather than left to complete, so a burst of typing produces
    /// one request at the end of it rather than one per pause. A cancelled wait is the ordinary case
    /// here, not an error.
    /// </remarks>
    private async Task ScheduleDiagnosticsAsync()
    {
        if (diagnostics is { } previous)
        {
            await previous.CancelAsync();
            previous.Dispose();
        }

        var source = new CancellationTokenSource();

        diagnostics = source;

        try
        {
            await Task.Delay(DiagnosticsDelay, source.Token);

            var report = await Stylesheet.ValidateAsync(Draft, source.Token);

            if (source.Token.IsCancellationRequested) return;

            Diagnostics = report?.Diagnostics ?? [];

            StateHasChanged();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a later keystroke, which is what the debounce is for.
        }
    }

    /// <summary>
    /// Loads a handful of published pages for the preview picker.
    /// </summary>
    /// <remarks>
    /// The top of the tree rather than a search: a stylesheet is judged on the pages most visitors
    /// land on, and the home page is nearly always the first of them.
    /// </remarks>
    private async Task LoadPreviewPagesAsync()
    {
        var tree = await Pages.GetTreeAsync(parentId: null, depth: 2);
        var pages = new List<PageSummary>();

        Flatten(tree, pages);

        PreviewPages = pages;
        PreviewPageId ??= pages.Count > 0 ? pages[0].Id : null;
    }

    private static void Flatten(IReadOnlyList<PageTreeNode> nodes, List<PageSummary> into)
    {
        foreach (var node in nodes)
        {
            into.Add(node.Page);

            Flatten(node.Children, into);
        }
    }
}
