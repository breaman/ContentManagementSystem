using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// One item picked from the media library, with its placement-specific alt text, focal point, and
/// crop (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "media", "mediaId": 812, "altOverride": null,
/// "focalPoint": { "x": 0.5, "y": 0.33 }, "crop": { "x": 0, "y": 0.1, "w": 1, "h": 0.8 } }</c>.
/// The payload holds an id and nothing else about the file: no URL, no dimensions, no alt text of
/// its own. Copying any of that into the page is what makes replacing an image in the library leave
/// stale copies behind on every page that used it.
/// <para>
/// Configuration keys: <c>allowedTypes</c>, <c>minWidth</c>, <c>aspectRatio</c>, and <c>sizes</c>.
/// </para>
/// <para>
/// <strong>Completed in P5.</strong> What this class checks is everything the payload can answer on
/// its own: the shape, the geometry, and the reference. The three restrictions are checked on the
/// publish path instead, beside <c>allowedTemplates</c> and the reusable-content <c>allowedTypes</c>
/// — a field type is a stateless singleton with no database, so "does item 812 exist, and how wide
/// is it" is not a question it can ask (spec section 7). The picture itself is
/// <c>MediaRenderer</c>'s, which resolves the item and signs a rendition URL per candidate width
/// (spec section 13.6).
/// </para>
/// </remarks>
public sealed class MediaFieldType : FieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Media;

    /// <inheritdoc />
    public override string DisplayName => "Media";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.ReferenceBearing;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema => MediaValue.PickerSettings;


    /// <inheritdoc />
    /// <remarks>
    /// The picked item, not the geometry: a value carrying a focal point but no <c>mediaId</c> is
    /// leftover state from a cleared picker, and treating it as filled would fail a publish on a
    /// property the editor believes is empty.
    /// </remarks>
    protected override string PayloadMember => MediaValue.MediaIdMember;

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        List<ValidationDiagnostic>? diagnostics = null;

        MediaValue.Validate(property, path: null, ref diagnostics);

        return Result(diagnostics);
    }

    /// <inheritdoc />
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value) =>
        MediaValue.ExtractReferences(value, path: null);

    /// <inheritdoc />
    /// <remarks>
    /// Implemented even though duplication never remaps media — the copy references the same item
    /// rather than duplicating the bytes (spec section 14.12). The delegate decides that, not this
    /// method, and a field type that quietly ignored a replacement it was handed would be a hole the
    /// day something else needs one.
    /// </remarks>
    public override JsonNode? RemapReferences(JsonElement value, ReferenceRemapper remap)
    {
        ArgumentNullException.ThrowIfNull(remap);

        if (ReferenceRemapping.Clone(value) is not { } copy) return null;

        return ReferenceRemapping.RemapMember(
            copy, MediaValue.MediaIdMember, ContentReferenceTargetType.Media, remap)
            ? copy
            : null;
    }
}
