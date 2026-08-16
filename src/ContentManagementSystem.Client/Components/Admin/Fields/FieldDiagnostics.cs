using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

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
/// What validation said about one zone, or about one value inside it.
/// </summary>
/// <param name="Errors">Everything blocking a publish, in the order validation found it.</param>
/// <param name="Warnings">Everything non-blocking.</param>
/// <remarks>
/// Introduced by the editing canvas (P6-05) for a zone card and generalised by the field editors
/// (P6-06 onwards) to any value with a payload path, since a block inside a block list needs exactly
/// the same two lists narrowed to exactly the same shape.
/// </remarks>
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

    /// <summary>How many diagnostics there are altogether.</summary>
    public int Count => Errors.Count + Warnings.Count;

    /// <summary>
    /// Narrows to the diagnostics naming a payload path, or something beneath it.
    /// </summary>
    /// <param name="path">The payload path of a value, such as <c>zones.body.items[2]</c>.</param>
    /// <returns>The diagnostics belonging to that value, empty when it has none.</returns>
    /// <remarks>
    /// This is what lets a container editor put a badge on the item that is actually wrong. Without
    /// it a twelve-block zone can only say "3 problems" and leave an editor to open all twelve.
    /// </remarks>
    public ZoneDiagnostics Within(string path)
    {
        if (!Any) return Empty;

        var errors = Errors.Where(diagnostic => Covers(path, diagnostic.Property)).ToList();
        var warnings = Warnings.Where(diagnostic => Covers(path, diagnostic.Property)).ToList();

        return errors.Count == 0 && warnings.Count == 0 ? Empty : new ZoneDiagnostics(errors, warnings);
    }

    /// <summary>
    /// Whether a diagnostic's path names a value at, or beneath, a payload path.
    /// </summary>
    /// <param name="path">The payload path of a value.</param>
    /// <param name="property">The path a diagnostic carries, which may be null.</param>
    /// <returns><see langword="true"/> when the diagnostic belongs to that value.</returns>
    /// <remarks>
    /// The match is bounded at a member or index boundary rather than being a bare
    /// <c>StartsWith</c>, so <c>zones.hero</c> does not claim what was said about <c>zones.heroine</c>
    /// — an off-by-one that would be invisible until two zone keys happened to share a prefix.
    /// </remarks>
    public static bool Covers(string path, string? property)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (property is not { Length: > 0 } candidate) return false;

        if (!candidate.StartsWith(path, StringComparison.Ordinal)) return false;

        return candidate.Length == path.Length || candidate[path.Length] is '.' or '[';
    }
}
