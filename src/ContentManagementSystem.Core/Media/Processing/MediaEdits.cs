using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Core.Media.Processing;

/// <summary>A point in an image, in fractions of its width and height.</summary>
/// <param name="X">Horizontal position, 0 at the left edge and 1 at the right.</param>
/// <param name="Y">Vertical position, 0 at the top edge and 1 at the bottom.</param>
public readonly record struct NormalizedPoint(double X, double Y)
{
    /// <summary>The centre of the image, used when nothing else is specified.</summary>
    public static NormalizedPoint Center { get; } = new(0.5, 0.5);

    /// <summary>Whether both coordinates lie inside the image.</summary>
    public bool IsValid => X is >= 0 and <= 1 && Y is >= 0 and <= 1;
}

/// <summary>A rectangle of an image, in fractions of its width and height.</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width as a fraction of the whole.</param>
/// <param name="Height">Height as a fraction of the whole.</param>
/// <remarks>
/// Normalized rather than in pixels so the same crop survives a replacement with a
/// higher-resolution original and applies unchanged to a rendition of any size
/// (spec section 13.4).
/// </remarks>
public readonly record struct NormalizedRect(double X, double Y, double Width, double Height)
{
    /// <summary>The whole image.</summary>
    public static NormalizedRect Full { get; } = new(0, 0, 1, 1);

    /// <summary>Whether the rectangle has a positive size and lies inside the image.</summary>
    public bool IsValid =>
        X is >= 0 and <= 1 && Y is >= 0 and <= 1 &&
        Width is > 0 and <= 1 && Height is > 0 and <= 1 &&
        X + Width <= 1.0000001 && Y + Height <= 1.0000001;
}

/// <summary>Which way an image is mirrored.</summary>
public enum FlipDirection
{
    /// <summary>Not mirrored.</summary>
    None = 0,

    /// <summary>Mirrored left to right.</summary>
    Horizontal,

    /// <summary>Mirrored top to bottom.</summary>
    Vertical,
}

/// <summary>
/// The non-destructive edits applied to an image (task P5-10, spec section 13.4).
/// </summary>
/// <param name="Rotate">Clockwise rotation in degrees — 0, 90, 180, or 270.</param>
/// <param name="Flip">Mirroring, applied after rotation.</param>
/// <param name="Crop">Rectangle of the source to use, or null for all of it.</param>
/// <param name="FocalPoint">Point to keep visible when cropping to another aspect ratio.</param>
/// <remarks>
/// The same shape at both scopes: a library edit is this document on
/// <c>MediaItem.EditsJson</c> and a usage edit is this document on the <c>media</c> field value in a
/// page payload. One type, so "what a crop means" cannot come to mean two things
/// (spec section 13.4).
/// <para>
/// <strong>Free rotation is deliberately absent.</strong> Anything other than a right angle leaves
/// triangular gaps that have to be filled with something, and there is no answer to "with what" that
/// is right for every image — so the operation set stops where the ambiguity starts.
/// </para>
/// <para>
/// Order is fixed and part of the contract: rotate, then flip, then crop. A crop is stored against
/// what the editor was looking at, which is the rotated image; applying it first would move it.
/// </para>
/// </remarks>
public sealed record MediaEdits(
    [property: JsonPropertyName("rotate")] int Rotate = 0,
    [property: JsonPropertyName("flip")] FlipDirection Flip = FlipDirection.None,
    [property: JsonPropertyName("crop")] NormalizedRect? Crop = null,
    [property: JsonPropertyName("focalPoint")] NormalizedPoint? FocalPoint = null)
{
    /// <summary>An unedited image.</summary>
    public static MediaEdits None { get; } = new();

    /// <summary>Whether anything here changes the pixels.</summary>
    /// <remarks>
    /// A focal point alone does not: it steers a later crop rather than altering the image, so an
    /// item carrying only one still renders its original geometry at full size.
    /// </remarks>
    public bool IsIdentity => Rotate is 0 && Flip is FlipDirection.None && Crop is null;

    /// <summary>Whether every value is one the processor can act on.</summary>
    public bool IsValid =>
        Rotate is 0 or 90 or 180 or 270 &&
        Flip is FlipDirection.None or FlipDirection.Horizontal or FlipDirection.Vertical &&
        Crop is not { IsValid: false } &&
        FocalPoint is not { IsValid: false };

    /// <summary>
    /// Reads an edit document, treating anything unusable as no edits at all.
    /// </summary>
    /// <param name="json">The stored JSON, or null.</param>
    /// <returns>The edits, or <see cref="None"/>.</returns>
    /// <remarks>
    /// Falling back to <see cref="None"/> rather than throwing is the right failure here: this runs
    /// on the delivery path, and an image that renders unedited is a visibly wrong crop somebody
    /// fixes. An exception would be a broken page. The write path validates properly and refuses
    /// with <c>media.edits-invalid</c>, so nothing unusable should reach storage to begin with.
    /// </remarks>
    public static MediaEdits Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return None;

        try
        {
            var edits = JsonSerializer.Deserialize<MediaEdits>(json, SerializerOptions);

            return edits is { IsValid: true } ? edits : None;
        }
        catch (JsonException)
        {
            return None;
        }
    }

    /// <summary>Serializes edits for storage.</summary>
    /// <returns>The JSON document, or null when there is nothing to store.</returns>
    public string? ToJson() =>
        this == None ? null : JsonSerializer.Serialize(this, SerializerOptions);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
