using ContentManagementSystem.Client.Components.Admin.Pickers;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Reference;

/// <summary>
/// The <c>reusable</c> editor — what is placed here, and whether the placement follows it
/// (task P6-15, spec section 9).
/// </summary>
/// <remarks>
/// The control says the binding in a sentence rather than showing a checkbox labelled "pinned". Late
/// binding is the default and the more consequential of the two states — one publish of a shared
/// banner updating forty pages is the behaviour the whole feature exists for — and an author needs to
/// know which one they are looking at without having to know what "pinned" means.
/// <para>
/// The pin is set in the picker, where a version can be resolved, and released here, where nothing
/// needs resolving. That asymmetry is deliberate: releasing a pin is always possible, while creating
/// one requires an item with something published.
/// </para>
/// </remarks>
public partial class ReusablePlacementEditor : FieldEditorBase
{
    /// <summary>The reusable item placed here.</summary>
    private const string ReusableContentIdMember = "reusableContentId";

    /// <summary>The version this placement is pinned to, or null to follow the item.</summary>
    private const string PinnedVersionIdMember = "pinnedVersionId";

    [Inject]
    private IReusableClient Client { get; set; } = default!;

    /// <summary>Whether the picker is open.</summary>
    private bool IsPicking { get; set; }

    /// <summary>The placed item, once resolved.</summary>
    private ReusableContentSummary? Item { get; set; }

    /// <summary>Identity of the placed item, or null when nothing is placed.</summary>
    private int? ReusableContentId => StoredValue.ReadInt32(Value, ReusableContentIdMember);

    /// <summary>The version the placement is pinned to, or null when it follows the item.</summary>
    private int? PinnedVersionId => StoredValue.ReadInt32(Value, PinnedVersionIdMember);

    /// <summary>The block type keys the slot accepts.</summary>
    private IReadOnlyList<string> AllowedTypes => ConfiguredTextList(FieldSettingNames.AllowedTypes);

    /// <summary>The value the placement was last resolved against.</summary>
    /// <remarks>
    /// Keyed on the value rather than on whether an item was found: an empty slot and an item that
    /// has since been deleted both resolve to nothing, and a guard keyed on that would re-ask on
    /// every render for as long as the screen is open.
    /// </remarks>
    private string? _resolved;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (string.Equals(Value, _resolved, StringComparison.Ordinal)) return;

        await ResolveAsync();
    }

    /// <summary>Looks up the placed item so the control can name it.</summary>
    private async Task ResolveAsync()
    {
        _resolved = Value;
        Item = ReusableContentId is { } id ? (await Client.GetAsync(id))?.Summary : null;
    }

    /// <summary>Stores the placement, keeping any member this build did not write.</summary>
    private async Task OnPickedAsync(ReusablePick pick)
    {
        IsPicking = false;
        Item = pick.Item;

        await WriteAsync(StoredValue.Write(Value, FieldTypeKey, stored =>
        {
            stored[ReusableContentIdMember] = pick.Item.Id;

            // Written as an explicit null rather than removed: null is what "follows the item" means
            // to the resolver, and the field type documents it as the member's default state rather
            // than as its absence.
            stored[PinnedVersionIdMember] = pick.PinnedVersionId;
        }));
    }

    /// <summary>Releases the pin, so the placement follows the item again.</summary>
    private Task UnpinAsync() =>
        WriteAsync(StoredValue.Write(Value, FieldTypeKey, stored => stored[PinnedVersionIdMember] = null));

    private Task ClearAsync()
    {
        Item = null;

        return WriteAsync(string.Empty);
    }
}
