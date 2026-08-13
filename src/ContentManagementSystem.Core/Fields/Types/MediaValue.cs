using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// One picked image or file, as <c>media</c> stores it and <c>mediaList</c> repeats it
/// (spec sections 6.2 and 7.1).
/// </summary>
/// <remarks>
/// Shape: <c>{ "mediaId": 812, "altOverride": null, "focalPoint": { "x": 0.5, "y": 0.33 },
/// "crop": { "x": 0, "y": 0.1, "w": 1, "h": 0.8 } }</c>. The geometry is stored in fractions of the
/// image rather than pixels so a rendition of any size can apply it, and so re-uploading a
/// higher-resolution original does not move the crop.
/// <para>
/// A single item's rules live here rather than on <see cref="MediaFieldType"/> so the gallery
/// applies the identical ones; two copies of "is this a crop" would diverge the first time either
/// gained a rule.
/// </para>
/// </remarks>
internal static class MediaValue
{
    /// <summary>The identity of the picked item — the member that decides whether anything is picked.</summary>
    public const string MediaIdMember = "mediaId";

    /// <summary>Alternative text overriding the media item's own, for this placement only.</summary>
    public const string AltOverrideMember = "altOverride";

    /// <summary>The point to keep visible when a rendition crops to a different aspect ratio.</summary>
    public const string FocalPointMember = "focalPoint";

    /// <summary>The rectangle of the original to render, in fractions of its width and height.</summary>
    public const string CropMember = "crop";

    /// <summary>
    /// The settings that restrict which library items may be picked.
    /// </summary>
    /// <remarks>
    /// Shared for the same reason the item rules above are: <c>media</c> and <c>mediaList</c> pick
    /// from one library under one set of restrictions, and two copies would diverge the first time
    /// either gained a setting. All three are declared now and enforced in P5, when the library
    /// they read exists — see <see cref="FieldConfigurationSetting.NotEnforcedUntil"/> for why they
    /// are declared early rather than refused until then.
    /// </remarks>
    public static FieldConfigurationSchema PickerSettings { get; } = new(
        [
            FieldConfigurationSetting.TextList(
                "allowedTypes",
                "Media types an editor may pick, such as image or document. An empty list allows any of them.",
                notEnforcedUntil: "P5"),
            FieldConfigurationSetting.Integer(
                "minWidth",
                "Narrowest original, in pixels, that may be picked.",
                minimum: 1,
                notEnforcedUntil: "P5"),
            FieldConfigurationSetting.Text(
                "aspectRatio",
                "Aspect ratio a picked item must have. The syntax is settled in P5 with the crop editor.",
                notEnforcedUntil: "P5"),
        ]);

    /// <summary>
    /// Checks one picked item.
    /// </summary>
    /// <param name="value">The media value object.</param>
    /// <param name="path">Path of this value relative to the property, or null when it is the property.</param>
    /// <param name="diagnostics">Collected diagnostics, allocated on first use.</param>
    /// <remarks>
    /// Everything checkable from the payload alone. That the item <em>exists</em>, is not deleted,
    /// and satisfies <c>allowedTypes</c> / <c>minWidth</c> / <c>aspectRatio</c> needs the media
    /// library, so it arrives with it in P5.
    /// </remarks>
    public static void Validate(
        JsonElement value,
        string? path,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (value.ValueKind is not JsonValueKind.Object)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.Shape,
                "Expected a picked media item.",
                path);

            return;
        }

        if (!StoredId.TryRead(value, MediaIdMember, out _))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.ReferenceId,
                "This does not identify a media item.",
                RelativePaths.Member(path, MediaIdMember));
        }

        if (value.TryGetProperty(AltOverrideMember, out var alt) &&
            alt.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.Shape,
                "Alternative text must be text, or null to use the media item's own.",
                RelativePaths.Member(path, AltOverrideMember));
        }

        ValidateFocalPoint(value, path, ref diagnostics);
        ValidateCrop(value, path, ref diagnostics);
    }

    /// <summary>
    /// Reports the item this value points at.
    /// </summary>
    /// <param name="value">The media value object.</param>
    /// <param name="path">Path of this value relative to the property, or null when it is the property.</param>
    /// <returns>One reference, or none when nothing is picked.</returns>
    public static IEnumerable<ContentReference> ExtractReferences(JsonElement value, string? path)
    {
        if (StoredId.TryRead(value, MediaIdMember, out var mediaId))
        {
            yield return new ContentReference(ContentReferenceTargetType.Media, mediaId, path);
        }
    }

    private static void ValidateFocalPoint(
        JsonElement value,
        string? path,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!TryGetGeometry(value, FocalPointMember, out var focalPoint)) return;

        if (focalPoint.ValueKind is not JsonValueKind.Object ||
            !TryReadFraction(focalPoint, "x", out _) ||
            !TryReadFraction(focalPoint, "y", out _))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.MediaFocalPoint,
                "A focal point is an x and a y between 0 and 1.",
                RelativePaths.Member(path, FocalPointMember));
        }
    }

    private static void ValidateCrop(
        JsonElement value,
        string? path,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!TryGetGeometry(value, CropMember, out var crop)) return;

        var cropPath = RelativePaths.Member(path, CropMember);

        if (crop.ValueKind is not JsonValueKind.Object ||
            !TryReadFraction(crop, "x", out var x) ||
            !TryReadFraction(crop, "y", out var y) ||
            !TryReadFraction(crop, "w", out var width) ||
            !TryReadFraction(crop, "h", out var height))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.MediaCrop,
                "A crop is an x, y, w, and h between 0 and 1.",
                cropPath);

            return;
        }

        // A zero-width crop renders nothing and a crop running off the edge asks the pipeline for
        // pixels that do not exist; both are storable and neither is recoverable at render time.
        if (width <= 0 || height <= 0 || x + width > 1 || y + height > 1)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.MediaCrop,
                "The crop must have a positive size and lie inside the image.",
                cropPath);
        }
    }

    /// <summary>
    /// Reads an optional geometry member, treating an explicit null as absent.
    /// </summary>
    /// <param name="value">The media value object.</param>
    /// <param name="member">The member name.</param>
    /// <param name="geometry">The member when present.</param>
    /// <returns><see langword="true"/> when there is something to check.</returns>
    /// <remarks>
    /// Null is how the editor clears a crop, and the payload example in spec section 6.2 stores it
    /// that way, so a null here is "no crop" and not a malformed one.
    /// </remarks>
    private static bool TryGetGeometry(JsonElement value, string member, out JsonElement geometry) =>
        value.TryGetProperty(member, out geometry) && geometry.ValueKind is not JsonValueKind.Null;

    private static bool TryReadFraction(JsonElement geometry, string member, out double fraction)
    {
        if (geometry.TryGetProperty(member, out var value) &&
            value.ValueKind is JsonValueKind.Number &&
            value.TryGetDouble(out fraction) &&
            fraction is >= 0 and <= 1)
        {
            return true;
        }

        fraction = 0;

        return false;
    }
}
