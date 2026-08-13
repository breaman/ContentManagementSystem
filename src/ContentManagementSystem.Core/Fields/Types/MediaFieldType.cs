using System.Text.Json;

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
/// Configuration keys: the P5 additions <c>allowedTypes</c>, <c>minWidth</c>, and
/// <c>aspectRatio</c>.
/// </para>
/// <para>
/// <strong>Completed in P5.</strong> What is here is everything the payload can answer on its own:
/// the shape, the geometry, and — the part that matters — the reference. Reference extraction is
/// deliberately not deferred with the rest, because a media field that reported nothing until P5
/// would leave every page saved before then with no <c>ContentReference</c> row, and nothing would
/// go back and add them: where-used would under-report and cache invalidation would miss the pages
/// (spec section 7.3). Still to come with the media library: that the item exists and is not
/// deleted, the <c>allowedTypes</c> / <c>minWidth</c> / <c>aspectRatio</c> restrictions, the picker,
/// and the renderer that turns a crop into a rendition.
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
}
