using System.Text.Json;

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
/// Configuration keys: <c>required</c>, and the P4 addition <c>allowedTypes</c>.
/// </para>
/// <para>
/// <strong>Completed in P4</strong>: the picker, the resolver that turns an id into the published
/// version with its cycle and depth guards, the pinned-version badge and its "update to latest"
/// action, and the check that the item exists. Reference extraction ships now — see
/// <see cref="MediaFieldType"/> for why, and note that here it does double duty: the delete guard
/// and the publish-impact count in spec section 9.4 are both queries over the rows it produces.
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
                "A pinned version must be a version number, or null to follow the item.",
                PinnedVersionIdMember);
        }

        return Result(diagnostics);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One row, for the item, whether or not the placement is pinned. A pinned version is not a
    /// second reference: <c>ContentReference</c> records which entities a page depends on, and the
    /// entity is the item — the version is which of its snapshots to render, which the resolver
    /// reads back out of the payload. Recording the version here instead would break the
    /// where-used query the delete guard runs, since a pinned page would no longer appear as a user
    /// of the item it is pinned to.
    /// </remarks>
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value)
    {
        if (StoredId.TryRead(value, ReusableContentIdMember, out var reusableContentId))
        {
            yield return new ContentReference(
                ContentReferenceTargetType.ReusableContent,
                reusableContentId);
        }
    }
}
