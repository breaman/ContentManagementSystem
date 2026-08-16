using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// The generic zone form, and the publish controls beside it (task P2-23, now on the P6-05 canvas).
/// </summary>
/// <remarks>
/// The zones are laid out by <see cref="EditingCanvas"/> — grouped, ordered, and each card carrying
/// its own validation state — and the control inside each card comes from the field editor catalog
/// through <c>FieldEditorHost</c> (ADR-0014, P6-06 to P6-15). What this screen owns is the loop
/// around them: reading a page, folding the cards back into a payload envelope, and the four writes
/// in the action bar.
/// <para>
/// It holds each zone's value as the stored JSON envelope rather than as the text inside it, which
/// is what lets it stay ignorant of every field type: a <c>richText</c> value keeps its format
/// beside its text and a <c>media</c> value keeps a crop, and neither fact is this screen's.
/// </para>
/// <para>
/// The form is built from the revision the draft <em>captured</em>, never from the template's
/// current zones (spec section 8.5). A page authored before a zone was added has no value under that
/// key, and showing a control for it would quietly invite an editor to author against a schema their
/// content is not being judged by.
/// </para>
/// </remarks>
public partial class PageEditor : ComponentBase
{
    /// <summary>Identity of the page being edited, from the route.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes pages, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

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

    /// <summary>The last dry-run check, or null when none has been made since the last change.</summary>
    private PublishValidation? Validation { get; set; }

    /// <summary>The last outcome, sorted onto the zone cards it concerns.</summary>
    private CanvasDiagnostics Diagnostics { get; set; } = CanvasDiagnostics.Empty;

    /// <summary>Why the last write did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Anything non-blocking the last write reported.</summary>
    private IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>Heading for the error list, so a refusal says which action it refused.</summary>
    private string ErrorHeading { get; set; } = "That did not work";

    /// <summary>A short confirmation of the last successful action.</summary>
    private string? Notice { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Whether the caller may edit, which is what makes the canvas read-only.</summary>
    private bool CanEdit { get; set; }

    /// <summary>
    /// Whether the next publish should proceed past the warnings the last one reported.
    /// </summary>
    /// <remarks>
    /// Latched by a refused publish and cleared by any change, which is what turns spec section
    /// 22.2's resubmit-to-acknowledge into one visible decision: the button relabels itself to
    /// "Publish anyway" only after a person has been shown what they would be publishing past.
    /// </remarks>
    private bool AcknowledgeWarnings { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // Re-read when the route changes, but not over a persisted pre-render of this same page.
        if (Page?.Summary.Id != Id)
        {
            Page = await Client.GetAsync(Id);
            Slots = null;
        }

        if (Page is null) return;

        Slots ??= await Client.GetZonesAsync(Page.Summary.TemplateId, Page.TemplateRevision);
        Values = PlainSlotValues.Read(Page.ContentJson, Slots);

        CanEdit = await HoldsAnyAsync(CmsRoles.ContentEditors);
    }

    /// <summary>
    /// What the action bar says when there is nothing to report.
    /// </summary>
    /// <remarks>
    /// Only the all-clear: the canvas's own summary counts anything that is wrong, and a status line
    /// repeating it would be the second place an editor has to look to learn the same thing. The
    /// "Saved 14:32" this slot is really for arrives with autosave in P6-18.
    /// </remarks>
    private string? StatusLine => Validation is { CanPublish: true } ? "Ready to publish" : null;

    /// <summary>
    /// Takes a zone's new value, and retires everything the last check said about the old one.
    /// </summary>
    /// <remarks>
    /// A validation result describes a payload, not a page. The moment one card changes, the badge
    /// on the card beside it is describing content that no longer exists — and a stale green is worse
    /// than no green, because it is the one an editor believes.
    /// </remarks>
    private void OnZoneChanged(string key, string value)
    {
        Values[key] = value;

        Validation = null;
        Diagnostics = CanvasDiagnostics.Empty;
        AcknowledgeWarnings = false;
        Errors = null;
        Warnings = null;
    }

    /// <summary>Re-reads the page and rebuilds the form from what the server now holds.</summary>
    /// <remarks>
    /// The row version above all: a save can normalise the payload, and the next save's precondition
    /// has to be the token the server just issued rather than the one the form was built with.
    /// </remarks>
    private async Task ReloadAsync()
    {
        Page = await Client.GetAsync(Id);
        Slots = null;

        await OnParametersSetAsync();
    }

    private async Task SaveAsync() => await WriteAsync(
        "The draft was not saved",
        async () =>
        {
            var result = await Client.SaveDraftAsync(
                Id,
                new SaveDraftRequest(
                    PlainSlotValues.Build(
                        Page!.ContentJson,
                        Page.Summary.TemplateKey,
                        Page.TemplateRevision,
                        Slots ?? [],
                        Values),
                    Page.RowVersion));

            if (!result.IsSuccess) return result.Errors;

            Warnings = result.Warnings;
            Notice = "Draft saved. The published version is untouched.";

            // The saved payload is not the one the last check ran against — the check is stale by
            // definition, and keeping its verdict on the cards would answer for content nobody has
            // checked.
            Validation = null;

            // Re-read rather than patching the row version in place; see ReloadAsync.
            await ReloadAsync();

            return null;
        });

    private async Task ValidateAsync() => await WriteAsync(
        "The check could not be run",
        async () =>
        {
            var result = await Client.ValidateAsync(Id);

            if (!result.IsSuccess) return result.Errors;

            Validation = result.Value;

            return null;
        });

    private async Task PublishAsync() => await WriteAsync(
        "The page was not published",
        async () =>
        {
            var result = await Client.PublishAsync(Id, AcknowledgeWarnings);

            if (!result.IsSuccess)
            {
                // Only warnings blocked it, so offer the explicit second attempt rather than making
                // the editor wonder what to change — and show them, on the cards they belong to.
                // "Publish anyway" over an unstated warning is a button that asks for consent to
                // something nobody has been told (spec section 22.2).
                AcknowledgeWarnings = result.Warnings.Count > 0;
                Warnings = result.Warnings;

                return result.Errors;
            }

            AcknowledgeWarnings = false;
            Warnings = result.Value!.Warnings;
            Notice = $"Published v{result.Value.VersionNumber}.";
            Validation = null;
            Page = await Client.GetAsync(Id);

            return null;
        });

    private async Task UnpublishAsync() => await WriteAsync(
        "The page was not unpublished",
        async () =>
        {
            var result = await Client.UnpublishAsync(Id);

            if (!result.IsSuccess) return result.Errors;

            Notice = $"Retired v{result.Value!.UnpublishedVersionNumber} from the public site. " +
                "The draft is untouched.";
            Page = await Client.GetAsync(Id);

            return null;
        });

    /// <summary>
    /// Runs one write, clearing the previous outcome and reporting whatever this one produced.
    /// </summary>
    /// <param name="heading">What to call the failure if there is one.</param>
    /// <param name="write">The write, returning the errors that blocked it or null on success.</param>
    private async Task WriteAsync(string heading, Func<Task<IReadOnlyList<ApiDiagnostic>?>> write)
    {
        IsBusy = true;
        Errors = null;
        Warnings = null;
        Notice = null;
        ErrorHeading = heading;

        try
        {
            Errors = await write();
        }
        finally
        {
            IsBusy = false;

            // Whatever this write produced is what the cards now show. The dry-run check is the
            // fallback rather than the first choice: a refused save is about the payload in front of
            // the editor, while the last check may be about the one before it.
            Diagnostics = CanvasDiagnostics.From(
                Errors ?? Validation?.Errors,
                Warnings ?? Validation?.Warnings);
        }
    }

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
