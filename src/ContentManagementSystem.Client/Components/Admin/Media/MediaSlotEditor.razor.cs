using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Media;

/// <summary>
/// The <c>media</c> field's editor: pick an item, describe it for this page, and crop it for this
/// page (task P5-19, spec sections 7.1 and 13.4).
/// </summary>
/// <remarks>
/// <strong>Everything this control writes is usage-scope.</strong> The crop, the focal point, and
/// the alternative text it edits live in <em>this page's</em> payload, so they affect this placement
/// and no other — which is the distinction spec section 13.4 draws between a library edit and a
/// usage edit, and the whole of acceptance criterion P5 #7. Straightening a sideways photograph for
/// everybody is the media library's screen, not this one.
/// <para>
/// It binds to the stored value as JSON text, which is how the plain admin forms carry a value they
/// have no control for. That is deliberate rather than temporary: the payload is runtime-shaped data
/// with no CLR type, and a control that parsed it into a model would have to decide what to do with
/// members it did not recognise. Reading and rewriting the members it owns leaves everything else
/// exactly as it was found.
/// </para>
/// </remarks>
public partial class MediaSlotEditor : ComponentBase
{
    private const string MediaIdMember = "mediaId";
    private const string AltOverrideMember = "altOverride";
    private const string FocalPointMember = "focalPoint";
    private const string CropMember = "crop";

    [Inject]
    private IMediaClient Client { get; set; } = default!;

    /// <summary>The stored value as JSON text, empty when nothing has been authored.</summary>
    [Parameter]
    public string Value { get; set; } = string.Empty;

    /// <summary>Raised with the rewritten JSON whenever the placement changes.</summary>
    [Parameter]
    public EventCallback<string> ValueChanged { get; set; }

    /// <summary>The zone or block property key, for control ids.</summary>
    [Parameter]
    public string PropertyKey { get; set; } = string.Empty;

    /// <summary>Whether the surrounding form is read-only.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The picked item's id, or null when nothing is picked.</summary>
    private int? MediaId { get; set; }

    /// <summary>The picked item, or null while loading or when nothing is picked.</summary>
    private MediaDetail? Item { get; set; }

    /// <summary>Signed URLs for the picked item.</summary>
    private MediaLinks? Links { get; set; }

    /// <summary>Alternative text for this placement only.</summary>
    private string? AltOverride { get; set; }

    /// <summary>Whether this placement crops the picture.</summary>
    private bool HasCrop { get; set; }

    private double CropX { get; set; }

    private double CropY { get; set; }

    private double CropWidth { get; set; } = 1;

    private double CropHeight { get; set; } = 1;

    /// <summary>Whether this placement steers the crop with its own focal point.</summary>
    private bool HasFocalPoint { get; set; }

    private double FocalX { get; set; } = 0.5;

    private double FocalY { get; set; } = 0.5;

    /// <summary>Whether the picker is open.</summary>
    private bool IsPicking { get; set; }

    /// <summary>The last value this control wrote, so its own writes do not re-read as external.</summary>
    private string? _lastWritten;

    /// <inheritdoc />
    /// <remarks>
    /// Re-reads only when the value came from somewhere else. Without the guard, every keystroke
    /// this control writes would be parsed straight back in and would reset the box being typed in.
    /// </remarks>
    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Value, _lastWritten, StringComparison.Ordinal)) return;

        Read(Value);

        await LoadItemAsync();
    }

    /// <summary>Parses the stored value into the controls.</summary>
    /// <param name="json">The stored value as JSON text.</param>
    /// <remarks>
    /// Anything unreadable is treated as "nothing picked" rather than reported. A payload this
    /// control cannot parse is one the validator has already complained about against the same
    /// property, and a second complaint in the editor would put two messages on one defect.
    /// </remarks>
    private void Read(string json)
    {
        MediaId = null;
        AltOverride = null;
        HasCrop = false;
        HasFocalPoint = false;
        CropX = CropY = 0;
        CropWidth = CropHeight = 1;
        FocalX = FocalY = 0.5;

        if (string.IsNullOrWhiteSpace(json)) return;

        JsonNode? node;

        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        if (node is not JsonObject stored) return;

        if (stored[MediaIdMember]?.GetValueKind() is JsonValueKind.Number &&
            stored[MediaIdMember]!.GetValue<int>() is > 0 and var id)
        {
            MediaId = id;
        }

        AltOverride = stored[AltOverrideMember]?.GetValueKind() is JsonValueKind.String
            ? stored[AltOverrideMember]!.GetValue<string>()
            : null;

        if (stored[CropMember] is JsonObject crop)
        {
            HasCrop = true;
            CropX = Fraction(crop, "x", 0);
            CropY = Fraction(crop, "y", 0);
            CropWidth = Fraction(crop, "w", 1);
            CropHeight = Fraction(crop, "h", 1);
        }

        if (stored[FocalPointMember] is JsonObject focal)
        {
            HasFocalPoint = true;
            FocalX = Fraction(focal, "x", 0.5);
            FocalY = Fraction(focal, "y", 0.5);
        }
    }

    /// <summary>Loads the picked item and its signed URLs.</summary>
    private async Task LoadItemAsync()
    {
        if (MediaId is not { } id)
        {
            Item = null;
            Links = null;

            return;
        }

        Item = await Client.GetAsync(id);
        Links = (await Client.LinksAsync([id])).GetValueOrDefault(id);
    }

    /// <summary>Writes the controls back into the stored value and raises the change.</summary>
    /// <remarks>
    /// Rebuilt from the value that was read rather than from scratch, so a member some other tool
    /// wrote into this placement survives being edited here. Clearing the picked item writes an
    /// empty string, which the surrounding form treats as "remove the slot" — absent and null are
    /// different facts about a zone, and this is the one that means nobody chose anything.
    /// </remarks>
    private async Task WriteAsync()
    {
        if (MediaId is not { } id)
        {
            _lastWritten = string.Empty;

            await ValueChanged.InvokeAsync(string.Empty);

            return;
        }

        var stored = ParseOrNew();

        stored[ContentPayloadMembers.Type] = FieldTypeKeys.Media;
        stored[MediaIdMember] = id;
        stored[AltOverrideMember] = string.IsNullOrWhiteSpace(AltOverride) ? null : AltOverride;

        stored[CropMember] = HasCrop
            ? new JsonObject
            {
                ["x"] = CropX,
                ["y"] = CropY,
                ["w"] = CropWidth,
                ["h"] = CropHeight,
            }
            : null;

        stored[FocalPointMember] = HasFocalPoint
            ? new JsonObject { ["x"] = FocalX, ["y"] = FocalY }
            : null;

        _lastWritten = stored.ToJsonString();

        await ValueChanged.InvokeAsync(_lastWritten);
    }

    private JsonObject ParseOrNew()
    {
        if (string.IsNullOrWhiteSpace(Value)) return [];

        try
        {
            return JsonNode.Parse(Value) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task PickedAsync(MediaDetail item)
    {
        ArgumentNullException.ThrowIfNull(item);

        MediaId = item.Id;
        Item = item;
        Links = (await Client.LinksAsync([item.Id])).GetValueOrDefault(item.Id);
        IsPicking = false;

        await WriteAsync();
    }

    private async Task ClearAsync()
    {
        MediaId = null;
        Item = null;
        Links = null;

        await WriteAsync();
    }

    /// <summary>
    /// Reads a number out of a change event, falling back rather than throwing.
    /// </summary>
    /// <param name="value">What the control reported.</param>
    /// <param name="fallback">The value to keep when the control holds nothing usable.</param>
    /// <returns>The number to store.</returns>
    /// <remarks>
    /// A number input can hold text a browser refuses to parse — an empty box being the common one,
    /// mid-edit. Falling back keeps the payload well-formed while somebody is still typing; the
    /// value they end up with is the one that gets saved. Invariant culture, because these are
    /// fractions going into a JSON document rather than numbers being shown to anybody.
    /// </remarks>
    private static double Number(object? value, double fallback) =>
        double.TryParse(
            value?.ToString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;

    private static double Fraction(JsonObject owner, string member, double fallback) =>
        owner[member]?.GetValueKind() is JsonValueKind.Number ? owner[member]!.GetValue<double>() : fallback;
}
