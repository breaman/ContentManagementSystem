using System.Text;
using System.Text.Json;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
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
    /// <summary>
    /// Field types whose stored value is a single string this screen can safely round-trip.
    /// </summary>
    /// <remarks>
    /// Everything else is shown as stored JSON and written back verbatim. That is the honest
    /// behaviour for a plain editor: inventing a control for a media reference or a block list would
    /// mean inventing a shape for its value, and the first thing a real editor in Phase 6 would have
    /// to do is repair what this one wrote.
    /// </remarks>
    private static readonly HashSet<string> TextFieldTypes = new(StringComparer.Ordinal)
    {
        FieldTypeKeys.PlainText,
        FieldTypeKeys.MultilineText,
        FieldTypeKeys.RichText,
        FieldTypeKeys.Html,
    };

    /// <summary>Formatting for the read-only view of a value this screen cannot edit.</summary>
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

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
        Values = ReadValues(Page.ContentJson, Slots);

        CanEdit = await HoldsAnyAsync(CmsRoles.ContentEditors);
    }

    /// <summary>Whether the zone gets a plain editable control rather than a read-only one.</summary>
    private static bool Editable(string fieldTypeKey) => TextFieldTypes.Contains(fieldTypeKey);

    /// <summary>
    /// Pulls each zone's stored value into the string its control binds to.
    /// </summary>
    /// <param name="contentJson">The draft payload, as stored.</param>
    /// <param name="slots">The zones the captured revision declares.</param>
    /// <returns>One entry per zone, empty for one that has never been authored.</returns>
    /// <remarks>
    /// An unparseable payload yields empty controls rather than an exception. The alternative is an
    /// editor who cannot open the one page that needs fixing, which is the failure spec section 15.3
    /// forbids on the delivery side and which is no more acceptable here.
    /// </remarks>
    private static Dictionary<string, string> ReadValues(
        string contentJson,
        IReadOnlyList<CapturedSlot> slots)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        ContentPayload.TryParse(contentJson, out var payload);

        foreach (var slot in slots)
        {
            values[slot.Key] = payload?.TryGetZone(slot.Key, out var zone) is true
                ? ReadValue(zone, slot.FieldTypeKey)
                : string.Empty;
        }

        return values;
    }

    private static string ReadValue(JsonElement zone, string fieldTypeKey)
    {
        if (zone.ValueKind is not JsonValueKind.Object) return string.Empty;

        if (Editable(fieldTypeKey))
        {
            return zone.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        // Indented, because the only thing to do with a value this screen cannot render is read it.
        return JsonSerializer.Serialize(zone, IndentedJson);
    }

    /// <summary>
    /// Folds the controls back into a payload envelope.
    /// </summary>
    /// <remarks>
    /// An emptied control <em>removes</em> the zone rather than storing null. Absent means never
    /// authored and null means deliberately cleared, and the payload reader keeps them apart on
    /// purpose (spec section 6.2); writing null for a box somebody simply never filled in would tell
    /// the renderer a fallback was declined.
    /// </remarks>
    private string BuildPayload()
    {
        var builder = ContentPayload.TryParse(Page!.ContentJson, out var current) && current.IsObject
            ? new ContentPayloadBuilder(current)
            : new ContentPayloadBuilder(Page.Summary.TemplateKey, Page.TemplateRevision);

        foreach (var slot in Slots ?? [])
        {
            var typed = Values.GetValueOrDefault(slot.Key, string.Empty);

            if (string.IsNullOrEmpty(typed))
            {
                builder.RemoveZone(slot.Key);

                continue;
            }

            builder.SetZone(slot.Key, WriteValue(slot, typed, current));
        }

        return builder.BuildJson();
    }

    /// <summary>Builds one zone's stored value from what its control holds.</summary>
    private static string WriteValue(CapturedSlot slot, string typed, ContentPayload? current)
    {
        if (!Editable(slot.FieldTypeKey))
        {
            // Written back exactly as it was read, so a field type this screen cannot edit survives
            // a save made for some other zone.
            return typed;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString(ContentPayloadMembers.Type, slot.FieldTypeKey);

            if (slot.FieldTypeKey == FieldTypeKeys.RichText)
            {
                // Rich text is uninterpretable without its format, and the stored one is kept so a
                // save from this screen cannot silently reinterpret an author's HTML as markdown.
                writer.WriteString("format", StoredFormat(slot.Key, current));
            }

            writer.WriteString("value", typed);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>The rich-text format already stored for a zone, defaulting to markdown.</summary>
    private static string StoredFormat(string zoneKey, ContentPayload? current) =>
        current?.TryGetZone(zoneKey, out var zone) is true &&
        zone.ValueKind is JsonValueKind.Object &&
        zone.TryGetProperty("format", out var format) &&
        format.ValueKind is JsonValueKind.String &&
        format.GetString() is { Length: > 0 } stored
            ? stored
            : "markdown";

    private async Task SaveAsync() => await WriteAsync(
        "The draft was not saved",
        async () =>
        {
            var result = await Client.SaveDraftAsync(
                Id,
                new SaveDraftRequest(BuildPayload(), Page!.RowVersion));

            if (!result.IsSuccess) return result.Errors;

            Warnings = result.Warnings;
            Notice = "Draft saved. The published version is untouched.";

            // Re-read rather than patching the row version in place: a save can normalise the
            // payload, and the next save's precondition has to be the token the server just issued.
            Page = await Client.GetAsync(Id);
            Slots = null;
            await OnParametersSetAsync();

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
