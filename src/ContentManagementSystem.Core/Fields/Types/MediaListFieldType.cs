using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// An ordered gallery of media items (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "mediaList", "items": [ { "mediaId": 812, … }, … ] }</c> — the same item
/// shape <c>media</c> stores, repeated. Order is the author's and is preserved as written.
/// <para>
/// Configuration keys: <c>required</c>, <c>min</c>, <c>max</c>, and the P5 additions
/// <c>allowedTypes</c>, <c>minWidth</c>, <c>aspectRatio</c>.
/// </para>
/// <para>
/// The same item appearing twice is allowed. A gallery that repeats an image is unusual but not
/// wrong, and refusing it would be the field type overruling the author on a matter of taste.
/// </para>
/// <para>
/// <strong>Completed in P5</strong>, alongside <see cref="MediaFieldType"/> and for the same
/// reasons.
/// </para>
/// </remarks>
public sealed class MediaListFieldType : ListFieldTypeBase
{
    /// <summary>The member holding the gallery.</summary>
    public const string ItemsMember = "items";

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.MediaList;

    /// <inheritdoc />
    public override string DisplayName => "Media list";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.ReferenceBearing;

    /// <inheritdoc />
    protected override string PayloadMember => ItemsMember;

    /// <inheritdoc />
    protected override string ItemNoun => "images";

    /// <inheritdoc />
    protected override void ValidateItems(
        JsonElement items,
        FieldConfiguration configuration,
        ValidationMode mode,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            MediaValue.Validate(item, ItemPath(index), ref diagnostics);
            index++;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each item is reported at its own position, so a broken-reference report can name the third
    /// image in a gallery rather than the gallery.
    /// </remarks>
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value)
    {
        if (GetValue(value) is not { ValueKind: JsonValueKind.Array } items) yield break;

        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            foreach (var reference in MediaValue.ExtractReferences(item, ItemPath(index)))
            {
                yield return reference;
            }

            index++;
        }
    }
}
