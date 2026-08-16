using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>How bad the worst thing said about one zone is.</summary>
public enum ZoneSeverity
{
    /// <summary>Nothing was said about the zone.</summary>
    None,

    /// <summary>Something worth reading, which does not block a publish.</summary>
    Warning,

    /// <summary>Something that blocks a publish.</summary>
    Error,
}

/// <summary>
/// What validation said about one zone.
/// </summary>
/// <param name="Errors">Everything blocking a publish, in the order validation found it.</param>
/// <param name="Warnings">Everything non-blocking.</param>
public sealed record ZoneDiagnostics(
    IReadOnlyList<ApiDiagnostic> Errors,
    IReadOnlyList<ApiDiagnostic> Warnings)
{
    /// <summary>Nothing at all, which is what most zones have to show.</summary>
    public static readonly ZoneDiagnostics Empty = new([], []);

    /// <summary>The worst of what was said, which is what the card's badge reports.</summary>
    public ZoneSeverity Severity => Errors.Count > 0
        ? ZoneSeverity.Error
        : Warnings.Count > 0
            ? ZoneSeverity.Warning
            : ZoneSeverity.None;

    /// <summary>Whether there is anything at all to show.</summary>
    public bool Any => Errors.Count > 0 || Warnings.Count > 0;
}

/// <summary>
/// Sorts a validation result onto the zone cards it concerns (task P6-05, spec section 14.6).
/// </summary>
/// <remarks>
/// A publish check reports one flat list of diagnostics, each naming the payload path it was found
/// at — <c>zones.hero.items[0].properties.headline</c>. The canvas needs the same information keyed
/// the other way round, because a card that cannot say "this one is why publishing is refused" sends
/// an editor to read a list at the bottom of the screen and count zones.
/// <para>
/// <strong>Anything that names no zone is kept, not dropped.</strong> A URL collision, a disabled
/// template, a missing meta description: none of them belong to a card, and all of them block or
/// warn. They are reported together above the canvas, which is the only place left that an editor
/// will actually read.
/// </para>
/// </remarks>
public sealed class CanvasDiagnostics
{
    private static readonly string ZonePrefix = ContentPayloadMembers.Zones + ".";

    private readonly Dictionary<string, List<ApiDiagnostic>> _errors = new(StringComparer.Ordinal);

    private readonly Dictionary<string, List<ApiDiagnostic>> _warnings = new(StringComparer.Ordinal);

    private readonly List<ApiDiagnostic> _pageErrors = [];

    private readonly List<ApiDiagnostic> _pageWarnings = [];

    private CanvasDiagnostics()
    {
    }

    /// <summary>Nothing has been checked yet, which is how a freshly opened page starts.</summary>
    public static CanvasDiagnostics Empty { get; } = new();

    /// <summary>Everything blocking a publish, wherever it was found.</summary>
    public int TotalErrors { get; private set; }

    /// <summary>Everything non-blocking, wherever it was found.</summary>
    public int TotalWarnings { get; private set; }

    /// <summary>Whether anything at all was reported.</summary>
    public bool Any => TotalErrors > 0 || TotalWarnings > 0;

    /// <summary>
    /// Sorts a validation result by zone.
    /// </summary>
    /// <param name="errors">The blocking diagnostics, or null when there are none.</param>
    /// <param name="warnings">The non-blocking diagnostics, or null when there are none.</param>
    /// <returns>The diagnostics, addressable by zone key.</returns>
    public static CanvasDiagnostics From(
        IReadOnlyList<ApiDiagnostic>? errors,
        IReadOnlyList<ApiDiagnostic>? warnings)
    {
        if (errors is not { Count: > 0 } && warnings is not { Count: > 0 })
        {
            return Empty;
        }

        var sorted = new CanvasDiagnostics();

        sorted.Add(errors, sorted._errors, sorted._pageErrors);
        sorted.Add(warnings, sorted._warnings, sorted._pageWarnings);

        sorted.TotalErrors = errors?.Count ?? 0;
        sorted.TotalWarnings = warnings?.Count ?? 0;

        return sorted;
    }

    /// <summary>
    /// Reads the zone a payload path sits in.
    /// </summary>
    /// <param name="path">The diagnostic's <see cref="ApiDiagnostic.Property"/>.</param>
    /// <returns>The zone key, or null when the path names no zone.</returns>
    /// <remarks>
    /// Only the first segment after <c>zones</c> is wanted, however deep the rest of the path goes: a
    /// diagnostic about the third property of the second block still belongs to the card the editor
    /// can see. The block and property halves of the path are what P6-20's deep links will use to go
    /// the rest of the way in.
    /// </remarks>
    public static string? ZoneKeyOf(string? path)
    {
        if (path is null || !path.StartsWith(ZonePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = path.AsSpan(ZonePrefix.Length);
        var end = rest.IndexOfAny('.', '[');

        // "zones." and "zones..value" name no zone. Answering the empty string instead would file
        // them under a card key that can never exist, which is the one bucket nothing renders.
        if (rest.IsEmpty || end == 0)
        {
            return null;
        }

        return end < 0 ? rest.ToString() : rest[..end].ToString();
    }

    /// <summary>What was said about one zone.</summary>
    /// <param name="zoneKey">The zone's payload key.</param>
    /// <returns>Its diagnostics, empty when it has none.</returns>
    public ZoneDiagnostics For(string zoneKey)
    {
        var errors = _errors.TryGetValue(zoneKey, out var blocking) ? blocking : [];
        var warnings = _warnings.TryGetValue(zoneKey, out var advisory) ? advisory : [];

        return errors.Count == 0 && warnings.Count == 0
            ? ZoneDiagnostics.Empty
            : new ZoneDiagnostics(errors, warnings);
    }

    /// <summary>
    /// Everything no card on the canvas will show.
    /// </summary>
    /// <param name="placed">The zone keys the canvas is drawing a card for.</param>
    /// <returns>The page-level diagnostics, plus any that name a zone that is not being drawn.</returns>
    /// <remarks>
    /// The second half is the one that matters. A payload can hold a zone its template revision no
    /// longer declares, and validation says so (spec section 15.3) — against a zone key with no card
    /// to attach it to. Bucketed and then never rendered, that warning would be *more* hidden than
    /// before the canvas existed, so anything with nowhere to go is reported above the cards.
    /// </remarks>
    public ZoneDiagnostics Unplaced(IReadOnlyCollection<string> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        return new ZoneDiagnostics(
            [.. _pageErrors, .. Orphaned(_errors, placed)],
            [.. _pageWarnings, .. Orphaned(_warnings, placed)]);
    }

    private static IEnumerable<ApiDiagnostic> Orphaned(
        Dictionary<string, List<ApiDiagnostic>> byZone,
        IReadOnlyCollection<string> placed) =>
        byZone
            .Where(bucket => !placed.Contains(bucket.Key))
            .OrderBy(bucket => bucket.Key, StringComparer.Ordinal)
            .SelectMany(bucket => bucket.Value);

    private void Add(
        IReadOnlyList<ApiDiagnostic>? diagnostics,
        Dictionary<string, List<ApiDiagnostic>> byZone,
        List<ApiDiagnostic> page)
    {
        foreach (var diagnostic in diagnostics ?? [])
        {
            if (ZoneKeyOf(diagnostic.Property) is not { } zoneKey)
            {
                page.Add(diagnostic);

                continue;
            }

            if (!byZone.TryGetValue(zoneKey, out var bucket))
            {
                byZone[zoneKey] = bucket = [];
            }

            bucket.Add(diagnostic);
        }
    }
}
