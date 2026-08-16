using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;

namespace ContentManagementSystem.Client.Components.Admin.Reusable;

/// <summary>
/// The reusable item's property form, its where-used panel, and the publish-impact confirmation
/// (task P4-11, spec section 9.4).
/// </summary>
/// <remarks>
/// The same plain form the page editor uses — every property a textarea, real editors in Phase 6 —
/// wrapped around the one thing that makes reusable content different: <strong>publishing here
/// changes pages nobody on this screen is editing.</strong> Every irreversible action is therefore
/// staged: the screen asks the server what would be affected, shows it, and only then sends the
/// acknowledged request.
/// <para>
/// The confirmation is not the guard. The server refuses an unacknowledged publish whose blast
/// radius is non-zero, so a screen that skipped the dialog — or a script that never had one — cannot
/// change forty pages silently. What the dialog adds is that the person who did it saw the number
/// first.
/// </para>
/// <para>
/// The form is built from the revision the draft <em>captured</em>, never from the block type's
/// current properties (spec section 8.5), for the reason the page editor gives: showing a control
/// for a property the content is not being judged against invites authoring into a void.
/// </para>
/// </remarks>
public partial class ReusableEditor : ComponentBase
{
    /// <summary>Identity of the item being edited, from the route.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes items, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IReusableClient Client { get; set; } = default!;

    /// <summary>Who is signed in, so the screen can hide controls they cannot use.</summary>
    [CascadingParameter]
    private Task<AuthenticationState>? AuthenticationState { get; set; }

    /// <summary>The item and its draft, or null while loading.</summary>
    [PersistentState]
    public ReusableContentDetail? Item { get; set; }

    /// <summary>Properties as the draft's revision captured them, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<CapturedSlot>? Slots { get; set; }

    /// <summary>The item's version history, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<ReusableVersionSummary>? Versions { get; set; }

    /// <summary>What currently places this item, or null while loading.</summary>
    [PersistentState]
    public ReferenceImpact? Impact { get; set; }

    /// <summary>What each property's control holds, keyed by property key.</summary>
    private Dictionary<string, string> Values { get; set; } = new(StringComparer.Ordinal);

    /// <summary>The last dry-run check, or null when none has been made since the last change.</summary>
    private ReusablePublishValidation? Validation { get; set; }

    /// <summary>The action waiting on an explicit confirmation, or null when none is.</summary>
    private PendingAction? Pending { get; set; }

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

    /// <summary>How the standing where-used panel phrases what the affected pages would do.</summary>
    private string ImpactVerb => Item?.Summary.PublishedVersionNumber is null
        ? "will start showing it once this is published"
        : "will change when this is published";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        // Re-read when the route changes, but not over a persisted pre-render of this same item.
        if (Item?.Summary.Id != Id)
        {
            Item = await Client.GetAsync(Id);
            Slots = null;
            Versions = null;
            Impact = null;
        }

        if (Item is null) return;

        Slots ??= await Client.GetPropertiesAsync(Item.Summary.BlockTypeId, Item.BlockTypeRevision);
        Versions ??= await Client.GetVersionsAsync(Id);
        Impact ??= await Client.WhereUsedAsync(Id);
        Values = PlainSlotValues.Read(Item.ContentJson, Slots);

        CanEdit = await HoldsAnyAsync(CmsRoles.ContentEditors);
    }

    /// <summary>Whether the property gets a plain editable control rather than a read-only one.</summary>
    private static bool Editable(string fieldTypeKey) => PlainSlotValues.Editable(fieldTypeKey);

    private async Task SaveAsync() => await WriteAsync(
        "The draft was not saved",
        async () =>
        {
            var result = await Client.SaveDraftAsync(
                Id,
                new SaveDraftRequest(
                    PlainSlotValues.Build(
                        Item!.ContentJson,
                        Item.Summary.BlockTypeKey,
                        Item.BlockTypeRevision,
                        Slots ?? [],
                        Values),
                    Item.RowVersion));

            if (!result.IsSuccess) return result.Errors;

            Warnings = result.Warnings;
            Notice = "Draft saved. Every page placing this item is untouched until you publish.";

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
            Impact = result.Value!.Impact;

            return null;
        });

    /// <summary>
    /// Stages a publish, or performs it outright when it would change nothing.
    /// </summary>
    /// <remarks>
    /// The impact is re-read here rather than taken from the standing panel: that one was loaded when
    /// the screen opened, and an editor who left the tab open while somebody else published three
    /// pages would be shown a number that was true an hour ago.
    /// </remarks>
    private async Task PublishAsync() => await WriteAsync(
        "The item was not published",
        async () =>
        {
            var check = await Client.ValidateAsync(Id);

            if (!check.IsSuccess) return check.Errors;

            Validation = check.Value;
            Impact = check.Value!.Impact;

            if (!check.Value.Impact.RequiresConfirmation) return await PublishConfirmedAsync();

            Pending = new PendingAction(
                PendingKind.Publish,
                check.Value.Impact,
                "will change",
                $"Publishing v{Item!.Summary.DraftVersionNumber} replaces what " +
                $"{check.Value.Impact.AffectedPageCount} published " +
                $"page{(check.Value.Impact.AffectedPageCount == 1 ? string.Empty : "s")} " +
                "currently shows. Those pages are not republished and their own drafts are untouched.",
                "Publish to all of them");

            return null;
        });

    private async Task UnpublishAsync() => await WriteAsync(
        "The item was not unpublished",
        async () =>
        {
            Impact = await Client.WhereUsedAsync(Id);

            if (!Impact.RequiresConfirmation) return await UnpublishConfirmedAsync();

            Pending = new PendingAction(
                PendingKind.Unpublish,
                Impact,
                "will render nothing in its place",
                $"Retiring this item leaves {Impact.AffectedPageCount} published " +
                $"page{(Impact.AffectedPageCount == 1 ? string.Empty : "s")} with an empty space " +
                "where it is placed. Nothing on those pages changes to explain the gap.",
                "Unpublish anyway");

            return null;
        });

    /// <summary>
    /// Deletes the item, which the server refuses while anything still places it.
    /// </summary>
    /// <remarks>
    /// No confirmation is staged for a delete that is going to be refused — the refusal <em>is</em>
    /// the answer, and it carries the list of what to fix first. A confirmation dialog in front of it
    /// would ask somebody to approve something that cannot happen.
    /// </remarks>
    private async Task DeleteAsync() => await WriteAsync(
        "The item was not deleted",
        async () =>
        {
            var result = await Client.DeleteAsync(Id);

            if (!result.IsSuccess)
            {
                Impact = await Client.WhereUsedAsync(Id);

                return result.Errors;
            }

            Notice = "Moved to the recycle bin. Its version history is kept.";

            await ReloadAsync();

            return null;
        });

    private async Task ConfirmAsync()
    {
        if (Pending is not { } pending) return;

        Pending = null;

        await WriteAsync(
            pending.Kind is PendingKind.Publish
                ? "The item was not published"
                : "The item was not unpublished",
            () => pending.Kind is PendingKind.Publish
                ? PublishConfirmedAsync()
                : UnpublishConfirmedAsync());
    }

    private void Cancel()
    {
        Pending = null;
        Notice = "Nothing was changed.";
    }

    private async Task<IReadOnlyList<ApiDiagnostic>?> PublishConfirmedAsync()
    {
        var result = await Client.PublishAsync(Id, acknowledgeWarnings: true);

        if (!result.IsSuccess) return result.Errors;

        Warnings = result.Value!.Warnings;
        Notice = $"Published v{result.Value.VersionNumber}. " +
            $"{result.Value.Impact.AffectedPageCount} published " +
            $"page{(result.Value.Impact.AffectedPageCount == 1 ? string.Empty : "s")} changed with it; " +
            $"{result.Value.Impact.PinnedPageCount} pinned " +
            $"page{(result.Value.Impact.PinnedPageCount == 1 ? string.Empty : "s")} did not.";
        Validation = null;

        await ReloadAsync();

        return null;
    }

    private async Task<IReadOnlyList<ApiDiagnostic>?> UnpublishConfirmedAsync()
    {
        var result = await Client.UnpublishAsync(Id, acknowledgeWarnings: true);

        if (!result.IsSuccess) return result.Errors;

        Notice = $"Retired v{result.Value!.UnpublishedVersionNumber}. " +
            $"{result.Value.Impact.AffectedPageCount} published " +
            $"page{(result.Value.Impact.AffectedPageCount == 1 ? string.Empty : "s")} " +
            "now render nothing where it was. The draft is untouched.";
        Validation = null;

        await ReloadAsync();

        return null;
    }

    /// <summary>Re-reads everything a write can have moved.</summary>
    /// <remarks>
    /// The row version above all: a save normalises the payload, and the next save's precondition has
    /// to be the token the server just issued rather than the one the form was built with.
    /// </remarks>
    private async Task ReloadAsync()
    {
        Item = await Client.GetAsync(Id);
        Slots = null;
        Versions = null;
        Impact = null;

        await OnParametersSetAsync();
    }

    /// <summary>
    /// Runs one write, clearing the previous outcome and reporting whatever this one produced.
    /// </summary>
    /// <param name="heading">What to call the failure if there is one.</param>
    /// <param name="write">The write, returning the errors that blocked it or null on success.</param>
    private async Task WriteAsync(string heading, Func<Task<IReadOnlyList<ApiDiagnostic>?>> write)
    {
        ArgumentNullException.ThrowIfNull(write);

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

    /// <summary>Which staged action a confirmation belongs to.</summary>
    private enum PendingKind
    {
        /// <summary>Publishing the draft over what every late-bound page shows.</summary>
        Publish = 0,

        /// <summary>Retiring the item, leaving a gap on every page placing it.</summary>
        Unpublish = 1,
    }

    /// <summary>An action shown to a person before it is allowed to happen.</summary>
    /// <param name="Kind">Which action is staged.</param>
    /// <param name="Impact">What it would touch, as at the moment it was staged.</param>
    /// <param name="Verb">How the where-used panel should phrase the consequence.</param>
    /// <param name="Description">The sentence above the list.</param>
    /// <param name="ConfirmLabel">What the confirming button says — never just "OK".</param>
    private sealed record PendingAction(
        PendingKind Kind,
        ReferenceImpact Impact,
        string Verb,
        string Description,
        string ConfirmLabel);
}
