using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// The generic zone form, and the publish controls beside it (task P2-23).
/// </summary>
/// <remarks>
/// <strong>Deliberately plain.</strong> Every zone is a textarea, whatever its field type, and the
/// real per-field-type editors — rich text, media pickers, the block canvas — arrive in Phase 6 with
/// the component resolution ADR 0014 set up. What this screen exists to prove is the loop underneath
/// them: a template's captured zones become controls, what is typed into them round-trips through
/// the payload envelope, and publishing snapshots it without disturbing the draft.
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

    /// <summary>Whether the caller may edit, which the fieldset reads.</summary>
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

    /// <summary>Whether the zone gets a plain editable control rather than a read-only one.</summary>
    /// <remarks>
    /// The rules for moving values between a payload and a textarea live in
    /// <see cref="PlainSlotValues"/>, shared with the reusable-content editor: a zone and a
    /// block-type property are the same thing to a payload, and two copies of those rules would
    /// eventually disagree about what an emptied box means.
    /// </remarks>
    private static bool Editable(string fieldTypeKey) => PlainSlotValues.Editable(fieldTypeKey);

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
                // the editor wonder what to change.
                AcknowledgeWarnings = result.Warnings.Count > 0;

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
