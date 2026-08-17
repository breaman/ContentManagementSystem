using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Client.Components.Admin.Properties;
using ContentManagementSystem.Client.Components.Admin.Shortcuts;
using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// The page editing screen: zones on the canvas, properties beside them, and everything that
/// happens when an editor stops typing (tasks P2-23, P6-05, P6-17 to P6-22).
/// </summary>
/// <remarks>
/// The zones are laid out by <see cref="EditingCanvas"/> and each control comes from the field
/// editor catalog (ADR-0014); the rest of a page — title, URL, SEO, ownership — is
/// <see cref="PropertiesPanel"/>'s. What this screen owns is the loop around them: reading a page,
/// folding both halves back into the two writes the API offers, and deciding what happens when one
/// of them is refused.
/// <para>
/// <strong>One save, two requests, in that order.</strong> The payload goes through the draft save,
/// which is conditional on a row version; the metadata goes through the patch, which writes title
/// and the SEO fields to the same draft row and therefore <em>moves</em> that row version. Doing it
/// the other way round would leave the screen holding a token the server had just superseded and
/// turn an editor's next keystroke into a conflict with themselves (task P6-18).
/// </para>
/// <para>
/// Everything is built from the revision the draft <em>captured</em>, never from the template's
/// current zones (spec section 8.5). A page authored before a zone was added has no value under that
/// key, and showing a control for it would quietly invite an editor to author against a schema their
/// content is not being judged by.
/// </para>
/// </remarks>
public partial class PageEditor : ComponentBase, IAsyncDisposable
{
    /// <summary>Path of the collocated interop module, relative to the host page.</summary>
    private const string ModulePath = "./Components/Admin/Pages/PageEditor.razor.js";

    /// <summary>Identity of the page being edited, from the route.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes pages, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Confirms what happened, for the writes worth a word rather than a banner.</summary>
    [Inject]
    private IToastService Toasts { get; set; } = default!;

    /// <summary>The clock autosave's debounce is measured on.</summary>
    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>Navigation, so leaving the screen can save first.</summary>
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>The browser's JavaScript runtime, used to send an editor to a zone.</summary>
    [Inject]
    private IJSRuntime Js { get; set; } = default!;

    /// <summary>Who is signed in, so the screen can hide controls they cannot use.</summary>
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    /// <summary>The page and its draft, or null while loading.</summary>
    [PersistentState]
    public PageDetail? Page { get; set; }

    /// <summary>Zones as the draft's revision captured them, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<CapturedSlot>? Slots { get; set; }

    /// <summary>What each zone's control holds, keyed by zone key.</summary>
    private Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What the properties panel is editing.</summary>
    private PageProperties? Properties { get; set; }

    /// <summary>Saves the draft twenty seconds after the typing stops, and on the way out.</summary>
    private AutosaveController? Autosave { get; set; }

    /// <summary>The last dry-run check, or null when none has been made since the last change.</summary>
    private PublishValidation? Validation { get; set; }

    /// <summary>The last outcome, sorted onto the zone cards it concerns.</summary>
    private CanvasDiagnostics Diagnostics { get; set; } = CanvasDiagnostics.Empty;

    /// <summary>Why the last write did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Anything non-blocking the last write reported.</summary>
    private IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>
    /// Why the last metadata patch did not happen, kept apart from the payload's diagnostics.
    /// </summary>
    /// <remarks>
    /// Two writes, two panes. A refused slug belongs beside the slug box and a refused zone belongs
    /// on its card; pooling them would print each message twice, once in the pane that cannot act on
    /// it.
    /// </remarks>
    private IReadOnlyList<ApiDiagnostic>? MetadataErrors { get; set; }

    /// <summary>Heading for the error list, so a refusal says which action it refused.</summary>
    private string ErrorHeading { get; set; } = "That did not work";

    /// <summary>Whether a write the editor is waiting on is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Whether the caller may edit, which is what makes the canvas read-only.</summary>
    private bool CanEdit { get; set; }

    /// <summary>The draft that beat this editor's save, or null when none has.</summary>
    private DraftState? Theirs { get; set; }

    /// <summary>Whether the shortcut reference dialog is showing (task P6-23).</summary>
    private bool IsShortcutHelpOpen { get; set; }

    /// <summary>Whether the publish dialog is showing.</summary>
    private bool IsPublishDialogOpen { get; set; }

    /// <summary>Whether the dry-run check behind the publish dialog is still running.</summary>
    private bool IsChecking { get; set; }

    /// <summary>Whether the unpublish confirmation is showing.</summary>
    private bool IsUnpublishDialogOpen { get; set; }

    /// <summary>
    /// The version this screen just retired, so it can be offered back.
    /// </summary>
    /// <remarks>
    /// The undo affordance of task P6-21, and it is deliberately an inline bar rather than a toast:
    /// a toast times out, and this is the one action on the screen worth taking back after reading
    /// what it did.
    /// </remarks>
    private int? Unpublished { get; set; }

    /// <summary>What the live region announces about the last check.</summary>
    private string? ValidationAnnouncement { get; set; }

    /// <summary>Where the editor's work has got to, as the status bar shows it.</summary>
    private AutosaveStatus SaveStatus => Autosave?.Status ?? AutosaveStatus.Clean;

    /// <summary>The imported interop module.</summary>
    private IJSObjectReference? _module;

    /// <summary>The registration that lets this screen save before a navigation completes.</summary>
    private IDisposable? _navigationGuard;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Autosave = new AutosaveController(Clock, SaveEverythingAsync, InvokeAsync);
        Autosave.Changed += OnAutosaveChanged;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The navigation guard is registered here rather than in <c>OnInitialized</c>, and that is not
    /// a style choice: a location-changing handler takes a navigation lock, which only an
    /// interactive renderer has. Registering it while the screen was still pre-rendering on the
    /// server would throw before a single zone reached the browser.
    /// </remarks>
    protected override void OnAfterRender(bool firstRender)
    {
        if (!firstRender || _navigationGuard is not null) return;

        // Covers navigation inside the backoffice. Closing the tab is the browser's own prompt,
        // armed by the save-state indicator, because the router never sees it.
        _navigationGuard = Navigation.RegisterLocationChangingHandler(OnLocationChangingAsync);
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // Re-read when the route changes, but not over a persisted pre-render of this same page.
        if (Page?.Summary.Id != Id)
        {
            Page = await Client.GetAsync(Id);
            Slots = null;

            // And the properties with it. The panel's model is the previous page's until something
            // replaces it, and a save would then patch this page with that page's title.
            Properties = null;
        }

        if (Page is null) return;

        Slots ??= await Client.GetZonesAsync(Page.Summary.TemplateId, Page.TemplateRevision);
        Values = PlainSlotValues.Read(Page.ContentJson, Slots);
        Properties ??= PageProperties.From(Page);

        CanEdit = await HoldsAnyAsync(CmsRoles.ContentEditors);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _navigationGuard?.Dispose();

        if (Autosave is not null)
        {
            Autosave.Changed -= OnAutosaveChanged;

            await Autosave.DisposeAsync();
        }

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (Exception exception) when (exception is JSException or JSDisconnectedException)
            {
                // The document is gone, and with it everything the reference pointed at.
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// What the action bar says when there is nothing else to report.
    /// </summary>
    /// <remarks>
    /// Only the all-clear: the canvas's own summary counts anything that is wrong, and a status line
    /// repeating it would be the second place an editor has to look to learn the same thing. The
    /// save state lives in the bar beside it, where it belongs.
    /// </remarks>
    private string? StatusLine => Validation is { CanPublish: true } ? "Ready to publish" : null;

    /// <summary>Takes a zone's new value, and retires everything the last check said about the old one.</summary>
    /// <remarks>
    /// A validation result describes a payload, not a page. The moment one card changes, the badge
    /// on the card beside it is describing content that no longer exists — and a stale green is worse
    /// than no green, because it is the one an editor believes.
    /// </remarks>
    private void OnZoneChanged(string key, string value)
    {
        Values[key] = value;

        Invalidate();
        Autosave?.Touch();
    }

    /// <summary>Takes an edit from the properties panel.</summary>
    private void OnPropertiesChanged()
    {
        Invalidate();
        Autosave?.Touch();
    }

    /// <summary>Retires the last check, and anything the last write said about it.</summary>
    private void Invalidate()
    {
        Validation = null;
        Diagnostics = CanvasDiagnostics.Empty;
        Errors = null;
        Warnings = null;
        MetadataErrors = null;
        ValidationAnnouncement = null;
    }

    /// <summary>Redraws when autosave's state moves.</summary>
    private void OnAutosaveChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Re-reads the page and rebuilds the form from what the server now holds.</summary>
    /// <remarks>
    /// The row version above all: a save can normalise the payload, and the next save's precondition
    /// has to be the token the server just issued rather than the one the form was built with.
    /// </remarks>
    private async Task ReloadAsync()
    {
        Page = await Client.GetAsync(Id);
        Slots = null;
        Properties = Page is null ? null : PageProperties.From(Page);

        await OnParametersSetAsync();
    }

    /// <summary>
    /// Writes everything the editor has changed: the payload first, then the metadata.
    /// </summary>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>What autosave should do next.</returns>
    /// <remarks>
    /// The order is a rule rather than a preference — see the note on the class. Both halves adopt
    /// the row version the server answers with, because both writes move it.
    /// </remarks>
    private async Task<AutosaveResult> SaveEverythingAsync(CancellationToken cancellationToken)
    {
        if (Page is null || !CanEdit) return AutosaveResult.Saved;

        var payload = await SavePayloadAsync(cancellationToken);

        if (payload.Outcome is not AutosaveOutcome.Saved) return payload;

        return await SaveMetadataAsync(cancellationToken);
    }

    /// <summary>Writes the zone values, and opens the conflict dialog if somebody got there first.</summary>
    private async Task<AutosaveResult> SavePayloadAsync(CancellationToken cancellationToken)
    {
        var result = await Client.SaveDraftAsync(
            Id,
            new SaveDraftRequest(BuildPayload(), Page!.RowVersion),
            cancellationToken);

        if (result.IsSuccess)
        {
            Warnings = result.Warnings;

            // The token, not the whole record: the payload the server stored is the one this screen
            // just sent, and replacing the record would undo anything typed while it was in flight.
            Page = Page with { RowVersion = result.Value!.Draft.RowVersion };

            return AutosaveResult.Saved;
        }

        // A conflict is the one refusal that hands back state — the draft that won — and the one
        // that must not be retried. It needs a decision from a person (task P6-19).
        if (result.Value is DraftSaveResult conflict)
        {
            Theirs = conflict.Draft;

            Report("The draft was not saved", result.Errors, result.Warnings);

            return new AutosaveResult(
                AutosaveOutcome.Refused,
                "Somebody else saved this page first.");
        }

        Report("The draft was not saved", result.Errors, result.Warnings);

        return new AutosaveResult(AutosaveOutcome.Refused, result.Errors.FirstOrDefault()?.Message);
    }

    /// <summary>Writes the title, URL, SEO, and editorial fields — and only what changed.</summary>
    private async Task<AutosaveResult> SaveMetadataAsync(CancellationToken cancellationToken)
    {
        if (Properties is null || !Properties.HasChanges(Page!)) return AutosaveResult.Saved;

        var result = await Client.PatchMetadataAsync(
            Id,
            Properties.ToPatch(Page!),
            cancellationToken);

        if (result.IsSuccess)
        {
            // The whole record this time: a patch normalises what it stores — a slug is slugified,
            // a note is trimmed — and the screen has to be comparing against what was actually
            // written or it will keep sending the same field forever.
            Page = result.Value;

            return AutosaveResult.Saved;
        }

        MetadataErrors = result.Errors;

        return new AutosaveResult(AutosaveOutcome.Refused, result.Errors.FirstOrDefault()?.Message);
    }

    /// <summary>Folds the zone controls back into a payload envelope.</summary>
    private string BuildPayload() => PlainSlotValues.Build(
        Page!.ContentJson,
        Page.Summary.TemplateKey,
        Page.TemplateRevision,
        Slots ?? [],
        Values);

    /// <summary>
    /// Runs whatever a keyboard shortcut asked for (task P6-23).
    /// </summary>
    /// <remarks>
    /// Every branch calls the same method the button beside it calls, so a shortcut cannot reach a
    /// path the visible control does not — including its permission checks. The two that a viewer
    /// cannot use are absent from the table they were matched against rather than guarded here.
    /// </remarks>
    private async Task RunShortcutAsync(string id)
    {
        switch (id)
        {
            case EditorShortcuts.ShowHelp:
                IsShortcutHelpOpen = true;

                break;

            case EditorShortcuts.Save when CanEdit && !IsBusy:
                await SaveAsync();

                break;

            case EditorShortcuts.Check when !IsBusy:
                await ValidateAsync();

                break;

            case EditorShortcuts.Publish when !IsBusy:
                await BeginPublishAsync();

                break;

            case EditorShortcuts.Preview:
                // A new tab, exactly as the link does: an editor who lost their unsaved form to a
                // preview would not press this twice.
                await Js.InvokeVoidAsync("open", $"/preview/{Id}", "_blank", "noopener");

                break;
        }
    }

    /// <summary>Saves now, because the editor asked.</summary>
    private async Task SaveAsync()
    {
        IsBusy = true;

        try
        {
            await Autosave!.FlushAsync();
        }
        finally
        {
            IsBusy = false;
        }

        if (SaveStatus.Phase is AutosavePhase.Saved)
        {
            // A toast rather than a banner: an explicit save is a thing the editor just did and is
            // already looking at, and the state that outlives the toast is in the status bar
            // (spec section 11.3).
            Toasts.ShowSuccess("Draft saved. The published version is untouched.");
        }
    }

    /// <summary>Saves before leaving the screen, so nothing typed is left behind.</summary>
    /// <remarks>
    /// It does not block the navigation. An editor who has decided to leave should leave; what the
    /// handler owes them is that the draft is written first — and if that write is refused, a toast
    /// on the next screen rather than a dialog on a page they have already given up on.
    /// </remarks>
    private async ValueTask OnLocationChangingAsync(LocationChangingContext context)
    {
        if (Autosave is null || !SaveStatus.HasUnsavedWork) return;

        await Autosave.FlushAsync();

        if (SaveStatus.Phase is not AutosavePhase.Saved)
        {
            Toasts.ShowWarning(
                "Your last changes to this page could not be saved. Go back to it to see why.",
                "Not saved");
        }
    }

    /// <summary>Runs the publish checks without publishing, and shows what they found.</summary>
    private async Task ValidateAsync()
    {
        IsBusy = true;

        try
        {
            var result = await Client.ValidateAsync(Id);

            if (!result.IsSuccess)
            {
                Report("The check could not be run", result.Errors, result.Warnings);

                return;
            }

            Validation = result.Value;
            Errors = null;
            Warnings = null;

            Diagnostics = CanvasDiagnostics.From(Validation!.Errors, Validation.Warnings);
            ValidationAnnouncement = Announce(Validation);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the publish dialog on a fresh check.
    /// </summary>
    /// <remarks>
    /// It saves first. The check runs against what the server holds, so a dialog opened over unsaved
    /// edits would report on the paragraph before the one the editor is looking at — and then
    /// publish the one they are looking at.
    /// </remarks>
    private async Task BeginPublishAsync()
    {
        Unpublished = null;
        IsPublishDialogOpen = true;
        IsChecking = true;

        try
        {
            await Autosave!.FlushAsync();
            await ValidateAsync();
        }
        finally
        {
            IsChecking = false;
        }
    }

    /// <summary>Publishes, with or without the warnings acknowledged.</summary>
    private async Task PublishAsync(bool acknowledgeWarnings)
    {
        IsBusy = true;

        try
        {
            var result = await Client.PublishAsync(Id, acknowledgeWarnings);

            if (!result.IsSuccess)
            {
                // Straight back into the dialog with what the attempt itself said, which may not be
                // what the dry run said a moment ago — the two run the same checks against the same
                // payload, and disagreeing means somebody else changed something in between.
                Validation = new PublishValidation(false, result.Errors, result.Warnings);
                Diagnostics = CanvasDiagnostics.From(result.Errors, result.Warnings);
                ValidationAnnouncement = Announce(Validation);

                return;
            }

            IsPublishDialogOpen = false;
            Validation = null;
            Errors = null;
            Warnings = result.Value!.Warnings;
            Diagnostics = CanvasDiagnostics.From(null, result.Value.Warnings);

            Autosave!.MarkSaved(Clock.GetUtcNow());
            Toasts.ShowSuccess($"Published v{result.Value.VersionNumber}.", "Live now");

            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Retires the page from the public site, once the editor has confirmed it.</summary>
    private async Task UnpublishAsync()
    {
        IsBusy = true;

        try
        {
            var result = await Client.UnpublishAsync(Id);

            IsUnpublishDialogOpen = false;

            if (!result.IsSuccess)
            {
                Report("The page was not unpublished", result.Errors, result.Warnings);

                return;
            }

            Unpublished = result.Value!.UnpublishedVersionNumber;

            Toasts.ShowWarning(
                $"v{Unpublished} is no longer on the public site. The draft is untouched.",
                "Taken down");

            await ReloadAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Saves this editor's version over the one that beat it.</summary>
    /// <remarks>
    /// Sent with the winner's row version, which is what makes it a deliberate overwrite rather than
    /// a second lost race. Nothing is destroyed that the version history does not still hold.
    /// </remarks>
    private async Task KeepMineAsync()
    {
        if (Theirs is null || Page is null) return;

        IsBusy = true;

        try
        {
            Page = Page with { RowVersion = Theirs.RowVersion };
            Theirs = null;

            await Autosave!.FlushAsync();

            if (SaveStatus.Phase is AutosavePhase.Saved)
            {
                Toasts.ShowSuccess("Your version is saved. Theirs is in the page's history.");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Replaces what is on screen with the draft that won.</summary>
    private async Task TakeTheirsAsync()
    {
        Theirs = null;

        IsBusy = true;

        try
        {
            await ReloadAsync();

            Autosave!.MarkSaved(Clock.GetUtcNow());
            Toasts.ShowInfo("You are now editing their version.");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Sends the editor to a zone card, from the publish dialog's deep link.</summary>
    /// <remarks>
    /// Closes the dialog first, for the obvious reason: a link that scrolled a card into view behind
    /// a modal would be a link to something the editor still cannot see or type into. The card is
    /// <c>tabindex="-1"</c> (task P6-05), so focus can land on it without adding a tab stop per zone.
    /// </remarks>
    private async Task GoToZoneAsync(string zoneKey)
    {
        IsPublishDialogOpen = false;

        try
        {
            _module ??= await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);

            await _module.InvokeVoidAsync("focusZone", $"zone-{zoneKey}");
        }
        catch (Exception exception) when (exception is JSException
                                              or JSDisconnectedException
                                              or InvalidOperationException
                                              or TaskCanceledException)
        {
            // No document to scroll. The dialog has closed and the card is on the page either way.
        }
    }

    /// <summary>Records a refusal of the payload and sorts it onto the cards it concerns.</summary>
    private void Report(
        string heading,
        IReadOnlyList<ApiDiagnostic> errors,
        IReadOnlyList<ApiDiagnostic> warnings)
    {
        ErrorHeading = heading;
        Errors = errors;
        Warnings = warnings;
        MetadataErrors = null;
        Validation = null;

        Diagnostics = CanvasDiagnostics.From(errors, warnings);
    }

    /// <summary>What the live region says about a check that has just finished.</summary>
    /// <remarks>
    /// Assertive, and phrased as the outcome rather than as a count on its own: somebody who pressed
    /// "check" is waiting to hear whether they can publish, and "3" is not that answer.
    /// </remarks>
    private static string Announce(PublishValidation validation) => validation switch
    {
        { CanPublish: true, Warnings.Count: 0 } => "Checked. Nothing is blocking this page.",
        { CanPublish: true } => $"Checked. Nothing is blocking this page, and " +
            $"{Plural(validation.Warnings.Count, "thing")} worth looking at.",
        { Errors.Count: 1 } => "Checked. One problem is stopping this page from being published.",
        _ => $"Checked. {validation.Errors.Count} problems are stopping this page from being published.",
    };

    private static string Plural(int count, string noun) =>
        count == 1 ? $"one {noun} is" : $"{count} {noun}s are";

    /// <summary>Whether the signed-in user holds one of the roles in an authorize list.</summary>
    private async Task<bool> HoldsAnyAsync(string roles)
    {
        if (AuthenticationState is null) return false;

        var user = (await AuthenticationState).User;

        return roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(user.IsInRole);
    }
}
