using System.Text.Json;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Media;

using Microsoft.EntityFrameworkCore;

// Both namespaces declare a ContentReference: the Data one is the stored row and the Shared one is
// what a field type reports while a payload is walked. This file only ever deals in the latter.
using ContentReference = ContentManagementSystem.Shared.Contracts.Fields.ContentReference;

namespace ContentManagementSystem.Core.Media.Library;

/// <summary>
/// Checks the media a page or a reusable item places, at publish time (tasks P5-19 and P5-21,
/// spec sections 13.6 and 13.7).
/// </summary>
/// <remarks>
/// <strong>Here rather than in the field type, and for a structural reason.</strong> A field type is
/// a stateless singleton with no database: it can tell that a value names item 812, and it cannot
/// tell whether 812 exists, what it is, how wide it is, or whether anybody has ever described it.
/// Every rule in this class needs the row, so every one of them lives on the publish path — the same
/// seam that already checks <c>allowedTemplates</c> for page references and <c>allowedTypes</c> for
/// reusable placements (spec section 7).
/// <para>
/// What the restrictions are checked against is the picture the page will <em>show</em>: the library
/// edits and this placement's own crop are applied first. That is what makes a 4:3 photograph legal
/// in a 16:9 slot once an editor has cropped it there, which is the workflow the crop editor exists
/// to support — and it is why the same item can satisfy one placement and fail another.
/// </para>
/// </remarks>
public interface IMediaContentValidator
{
    /// <summary>
    /// Checks every media placement in a payload.
    /// </summary>
    /// <param name="payload">The content being published.</param>
    /// <param name="schema">
    /// The captured schema the payload was authored against, or null when the revision could not be
    /// resolved. Null means no configured restriction can be read, so none is enforced — the
    /// existence and alt-text rules still apply, because neither needs a schema.
    /// </param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>What a publish should be told, in payload order.</returns>
    Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        ContentPayload payload,
        ContentSchema? schema,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IMediaContentValidator" />
/// <param name="context">The application database context.</param>
/// <param name="indexer">Walks the payload for the media references it holds.</param>
/// <param name="schemas">Resolves the block type revisions nested placements captured.</param>
/// <param name="options">The deployment's publish-time media policy.</param>
public sealed class MediaContentValidator(
    ApplicationDbContext context,
    IReferenceIndexer indexer,
    IContentSchemaCatalog schemas,
    MediaValidationOptions options) : IMediaContentValidator
{
    /// <summary>Setting naming the media kinds a placement accepts.</summary>
    private const string AllowedTypesSetting = "allowedTypes";

    /// <summary>Setting naming the narrowest picture a placement accepts.</summary>
    private const string MinWidthSetting = "minWidth";

    /// <summary>Setting naming the ratio a placed picture must have.</summary>
    private const string AspectRatioSetting = "aspectRatio";

    /// <inheritdoc />
    public async Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        ContentPayload payload,
        ContentSchema? schema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var references = indexer.Extract(payload)
            .Where(reference => reference.TargetType is ContentReferenceTargetType.Media)
            .ToList();

        if (references.Count == 0) return [];

        var targets = references.Select(reference => reference.TargetId).ToHashSet();

        // The query filter is left in place, so an item in the recycle bin reads as missing. That is
        // the right answer at publish time as well as at render time: publishing a page whose
        // picture has been withdrawn would put the section 15.3 placeholder on the public site.
        var items = await context.MediaItems
            .AsNoTracking()
            .Where(item => targets.Contains(item.Id))
            .Select(item => new PlacedItem(
                item.Id,
                item.MediaKind,
                item.OriginalFileName,
                item.Width,
                item.Height,
                item.AltText,
                item.IsDecorative,
                item.EditsJson))
            .ToDictionaryAsync(item => item.Id, cancellationToken)
            .ConfigureAwait(false);

        var diagnostics = new List<ValidationDiagnostic>();

        foreach (var reference in references)
        {
            if (!items.TryGetValue(reference.TargetId, out var item))
            {
                diagnostics.Add(new ValidationDiagnostic(
                    MediaCodes.NotFound,
                    $"This content places media item {reference.TargetId}, which no longer exists or " +
                    "is in the recycle bin.",
                    ValidationSeverity.Error,
                    reference.Path));

                continue;
            }

            // The stored value, not just the row: a placement's alternative-text override and its
            // crop are what several of the checks below are actually about.
            var placement = ReferencePath.Value(reference.Path, payload);
            var slot = ContentSlots.Resolve(reference.Path, payload, schema, schemas);

            CheckAltText(item, placement, reference, diagnostics);
            CheckAllowedTypes(item, slot, reference, diagnostics);
            CheckDimensions(item, placement, slot, reference, diagnostics);
        }

        return diagnostics;
    }

    /// <summary>
    /// The alt-text rule of spec section 13.7, applied to one placement.
    /// </summary>
    /// <remarks>
    /// Three sources satisfy it and they are checked in the order an editor would expect: the item is
    /// flagged decorative, the item carries its own alternative text, or this placement overrides it.
    /// The override matters — an image whose library description is wrong for one page's context is
    /// the case the override exists for, and a rule that ignored it would force editors to choose
    /// between an accurate library and a publishable page.
    /// <para>
    /// Only images. A PDF has nothing to describe, and demanding alternative text for one would
    /// train editors to type something meaningless into a required box, which is worse for a screen
    /// reader than an empty one.
    /// </para>
    /// </remarks>
    private void CheckAltText(
        PlacedItem item,
        JsonElement placement,
        ContentReference reference,
        List<ValidationDiagnostic> diagnostics)
    {
        if (item.Kind is not MediaKind.Image) return;
        if (item.IsDecorative) return;
        if (!string.IsNullOrWhiteSpace(item.AltText)) return;

        if (placement.ValueKind is JsonValueKind.Object &&
            placement.TryGetProperty("altOverride", out var alt) &&
            alt.ValueKind is JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(alt.GetString()))
        {
            return;
        }

        diagnostics.Add(new ValidationDiagnostic(
            MediaCodes.AltTextRequired,
            $"'{item.OriginalFileName}' has no alternative text and is not marked decorative. " +
            "Describe it in the media library, mark it decorative, or give this placement its own " +
            "description.",
            options.MissingAltTextSeverity,
            reference.Path));
    }

    /// <summary>Checks the placement against the <c>allowedTypes</c> its slot declares.</summary>
    /// <remarks>
    /// An error rather than a warning, matching the reusable-content rule it is modelled on: a slot
    /// restricted to images and filled with a spreadsheet renders through markup written for a
    /// picture, and the failure surfaces on the public site rather than here.
    /// </remarks>
    private static void CheckAllowedTypes(
        PlacedItem item,
        ContentPropertySchema? slot,
        ContentReference reference,
        List<ValidationDiagnostic> diagnostics)
    {
        if (slot is null) return;

        var allowed = slot.Configuration.GetStringArray(AllowedTypesSetting);

        if (allowed.Length == 0) return;

        var kind = item.Kind.ToString();

        // Case-insensitively, because the setting is written by hand into a configuration document
        // and "image" is what a developer types. The allowed-values list on the setting keeps the
        // spelling honest; this keeps the capitalisation from being a trap.
        if (allowed.Contains(kind, StringComparer.OrdinalIgnoreCase)) return;

        diagnostics.Add(new ValidationDiagnostic(
            FieldValidationCodes.NotAllowed,
            $"'{slot.Name}' accepts {string.Join(", ", allowed)}, but '{item.OriginalFileName}' is " +
            $"a {kind.ToLowerInvariant()}.",
            ValidationSeverity.Error,
            reference.Path));
    }

    /// <summary>
    /// Checks the placed picture's size and shape against <c>minWidth</c> and <c>aspectRatio</c>.
    /// </summary>
    /// <remarks>
    /// Measured after the library's edits and this placement's crop, for the reason the class
    /// summary gives. A non-image is skipped rather than failed: it has no dimensions, and reporting
    /// "0 px wide" beside the <c>allowedTypes</c> error that already named the real problem would put
    /// two diagnostics on one mistake.
    /// </remarks>
    private static void CheckDimensions(
        PlacedItem item,
        JsonElement placement,
        ContentPropertySchema? slot,
        ContentReference reference,
        List<ValidationDiagnostic> diagnostics)
    {
        if (slot is null || item.Kind is not MediaKind.Image) return;

        var minWidth = slot.Configuration.GetInt32(MinWidthSetting);
        var hasRatio = MediaGeometry.TryParseAspectRatio(
            slot.Configuration.GetString(AspectRatioSetting),
            out var ratio);

        if (minWidth is null && !hasRatio) return;

        var effective = MediaGeometry.Effective(
            item.Width,
            item.Height,
            MediaEdits.Parse(item.EditsJson),
            ReadCrop(placement));

        // An image whose dimensions were never recorded — an SVG, or a row written before the
        // pipeline probed them — cannot be judged, and a publish must not be refused over a
        // measurement nobody has.
        if (effective is not { } size) return;

        if (minWidth is { } floor && size.Width < floor)
        {
            diagnostics.Add(new ValidationDiagnostic(
                FieldValidationCodes.Min,
                $"'{slot.Name}' needs a picture at least {floor} px wide; '{item.OriginalFileName}' " +
                $"is {size.Width} px wide as placed here, after any cropping.",
                ValidationSeverity.Error,
                reference.Path));
        }

        if (hasRatio && !MediaGeometry.MatchesAspectRatio(size, ratio))
        {
            diagnostics.Add(new ValidationDiagnostic(
                FieldValidationCodes.NotAllowed,
                $"'{slot.Name}' needs a picture shaped {slot.Configuration.GetString(AspectRatioSetting)}; " +
                $"'{item.OriginalFileName}' is {size.Width}×{size.Height} " +
                $"({MediaGeometry.DescribeRatio(size)}) here. Crop it in the picker to fit.",
                ValidationSeverity.Error,
                reference.Path));
        }
    }

    /// <summary>Reads a placement's crop, treating anything unusable as absent.</summary>
    /// <remarks>
    /// Unusable geometry has already been reported by the field type against this same path. Reading
    /// it as absent here means the size checks judge the whole picture, which is what would actually
    /// be rendered once the malformed crop is ignored (spec section 15.3).
    /// </remarks>
    private static NormalizedRect? ReadCrop(JsonElement placement)
    {
        if (placement.ValueKind is not JsonValueKind.Object ||
            !placement.TryGetProperty("crop", out var crop) ||
            crop.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        if (Fraction(crop, "x") is not { } x ||
            Fraction(crop, "y") is not { } y ||
            Fraction(crop, "w") is not { } width ||
            Fraction(crop, "h") is not { } height)
        {
            return null;
        }

        var rect = new NormalizedRect(x, y, width, height);

        return rect.IsValid ? rect : null;
    }

    private static double? Fraction(JsonElement geometry, string member) =>
        geometry.TryGetProperty(member, out var value) &&
        value.ValueKind is JsonValueKind.Number &&
        value.TryGetDouble(out var fraction)
            ? fraction
            : null;

    /// <summary>The columns a placement is judged against.</summary>
    /// <param name="Id">Identity of the item.</param>
    /// <param name="Kind">What the file is.</param>
    /// <param name="OriginalFileName">The uploaded name, which is what an editor recognises it by.</param>
    /// <param name="Width">Pixel width of the stored original.</param>
    /// <param name="Height">Pixel height of the stored original.</param>
    /// <param name="AltText">The item's own alternative text.</param>
    /// <param name="IsDecorative">Whether the item renders <c>alt=""</c>.</param>
    /// <param name="EditsJson">The library-scope edits, which change how large the picture is.</param>
    private sealed record PlacedItem(
        int Id,
        MediaKind Kind,
        string OriginalFileName,
        int? Width,
        int? Height,
        string? AltText,
        bool IsDecorative,
        string? EditsJson);
}
