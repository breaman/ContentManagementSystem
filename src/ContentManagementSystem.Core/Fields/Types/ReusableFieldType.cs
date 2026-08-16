using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A placement of independently published reusable content — a footer, a promotional banner
/// (spec sections 7.1 and 9).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "reusable", "reusableContentId": 3, "pinnedVersionId": null }</c>.
/// <para>
/// A null <c>pinnedVersionId</c> is late binding, and it is the default: the page renders whatever
/// version of the item is published at the time it is served, which is what makes one publish of a
/// shared banner update forty pages without republishing any of them. A non-null one pins the
/// placement to a specific version, so that page alone stops following the item.
/// </para>
/// <para>
/// Configuration key: <c>allowedTypes</c>, holding <em>block type</em> keys, since a reusable item's
/// shape is a block type (spec section 9.1). Enforced by the publish check rather than here, for the
/// reason <c>allowedTemplates</c> is: a field type is a stateless singleton with no database, and
/// "what shape is item 3" cannot be answered from the stored value alone.
/// </para>
/// <para>
/// <c>pinnedVersionId</c> names a <em>version row</em> — the id <c>ReusableVersionSummary</c> hands
/// an editor — and not a version number. The resolver looks it up with the item id in the same
/// predicate, so a pin that quoted another item's version resolves to nothing rather than rendering
/// the wrong content under this item's cache tag.
/// </para>
/// <para>
/// Reference extraction ships in P1 — see <see cref="MediaFieldType"/> for why — and here it does
/// double duty: the delete guard and the publish-impact count of spec section 9.4 are both queries
/// over the rows it produces, which is why the pin travels on the edge rather than staying in the
/// payload.
/// </para>
/// </remarks>
public sealed class ReusableFieldType : FieldTypeBase
{
    /// <summary>The reusable item placed here.</summary>
    public const string ReusableContentIdMember = "reusableContentId";

    /// <summary>The version this placement is pinned to, or null to follow the item.</summary>
    public const string PinnedVersionIdMember = "pinnedVersionId";

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Reusable;

    /// <inheritdoc />
    public override string DisplayName => "Reusable content";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.ReferenceBearing;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = new(
        [
            FieldConfigurationSetting.TextList(
                "allowedTypes",
                "Block type keys this property's reusable content may be shaped by. An empty list allows any of them."),
        ]);


    /// <inheritdoc />
    protected override string PayloadMember => ReusableContentIdMember;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        List<ValidationDiagnostic>? diagnostics = null;

        if (!StoredId.TryRead(property, ReusableContentIdMember, out _))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.ReferenceId,
                "This does not identify a reusable content item.",
                ReusableContentIdMember);
        }

        var pinned = GetMember(property, PinnedVersionIdMember);

        if (pinned.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null) &&
            !StoredId.TryRead(pinned, out _))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.ReferenceId,
                "A pinned version names a version of this item, or is null to follow the item.",
                PinnedVersionIdMember);
        }

        return Result(diagnostics);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One row, for the item, whether or not the placement is pinned. A pinned version is not a
    /// second reference: <c>ContentReference</c> records which entities a page depends on, and the
    /// entity is the item — the version is which of its snapshots to render. Recording the version
    /// as the target instead would break the where-used query the delete guard runs, since a pinned
    /// page would no longer appear as a user of the item it is pinned to.
    /// <para>
    /// The pin therefore rides on the row rather than replacing it. That is what lets the
    /// publish-impact check of spec section 9.4 split forty referencing pages into the ones that
    /// will change and the two that will not, without opening a single payload.
    /// </para>
    /// </remarks>
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value)
    {
        if (!StoredId.TryRead(value, ReusableContentIdMember, out var reusableContentId)) yield break;

        // A pin is only a pin when it names something. An unreadable or non-positive value is
        // treated as late binding, which is the safe direction: the placement follows the item and
        // is reported as changing, where the reverse would quietly exclude a page from the impact
        // count of a publish that does change it.
        var pinned = StoredId.TryRead(GetMember(value, PinnedVersionIdMember), out var pinnedVersionId)
            ? pinnedVersionId
            : (int?)null;

        yield return new ContentReference(
            ContentReferenceTargetType.ReusableContent,
            reusableContentId,
            Path: null,
            IsPinned: pinned is not null,
            PinnedVersionId: pinned);
    }

    /// <inheritdoc />
    public override JsonNode? RemapReferences(JsonElement value, ReferenceRemapper remap)
    {
        ArgumentNullException.ThrowIfNull(remap);

        if (ReferenceRemapping.Clone(value) is not { } copy) return null;

        return ReferenceRemapping.RemapMember(
            copy, ReusableContentIdMember, ContentReferenceTargetType.ReusableContent, remap)
            ? copy
            : null;
    }
}
