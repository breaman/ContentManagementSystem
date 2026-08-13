using System.Collections.ObjectModel;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// One problem found in a payload, addressed to the place in the document that holds it.
/// </summary>
/// <remarks>
/// The difference from <see cref="ValidationDiagnostic"/> is who filled in the address. A field type
/// reports a path relative to the one value it was handed, because it cannot know where in the
/// document that value sits; the walk knows, and produces these.
/// <para>
/// <see cref="ZoneKey"/>, <see cref="BlockId"/>, and <see cref="PropertyKey"/> repeat what
/// <see cref="Path"/> already encodes, for a reason: the backoffice addresses a block by its GUID,
/// not by its index, so pointing an editor at the failure means handing over the id rather than
/// asking every client to parse the path back apart. It is also what makes "identifies the exact
/// zone, block id, and property" a literal assertion rather than an interpretation of one.
/// </para>
/// </remarks>
/// <param name="Code">Stable machine-readable discriminator, such as <c>field.maxLength</c>.</param>
/// <param name="Message">Human-readable explanation, phrased for a content editor.</param>
/// <param name="Severity">Whether this blocks the save or publish that produced it.</param>
/// <param name="Path">
/// Absolute payload path, such as <c>zones.hero.items[0].properties.headline</c>.
/// </param>
public sealed record ContentValidationDiagnostic(
    string Code,
    string Message,
    ValidationSeverity Severity,
    string Path)
{
    /// <summary>The zone the problem is in, or null when it concerns the envelope.</summary>
    public string? ZoneKey { get; init; }

    /// <summary>The stable id of the block the problem is in, when it is inside one.</summary>
    public Guid? BlockId { get; init; }

    /// <summary>The block-type property or zone key the problem is on, when it is on one.</summary>
    public string? PropertyKey { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Severity} {Path} [{Code}] {Message}";
}

/// <summary>
/// Everything one walk of a payload found.
/// </summary>
/// <remarks>
/// Warnings and errors travel together rather than being filtered apart at the source, because the
/// API contract in spec section 22.2 returns both — a save that succeeded with three orphaned zones
/// is a different thing to report than one that succeeded cleanly.
/// </remarks>
public sealed class ContentValidationReport
{
    private static readonly ReadOnlyCollection<ContentValidationDiagnostic> NoDiagnostics =
        new List<ContentValidationDiagnostic>().AsReadOnly();

    /// <summary>A report with nothing to say.</summary>
    public static ContentValidationReport Empty { get; } = new(NoDiagnostics);

    /// <summary>Creates a report over collected diagnostics.</summary>
    /// <param name="diagnostics">The diagnostics, in the order the walk found them.</param>
    public ContentValidationReport(IReadOnlyList<ContentValidationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        Diagnostics = diagnostics;
    }

    /// <summary>Everything found, in document order.</summary>
    public IReadOnlyList<ContentValidationDiagnostic> Diagnostics { get; }

    /// <summary>Whether nothing at all was reported, not even a warning.</summary>
    public bool IsValid => Diagnostics.Count == 0;

    /// <summary>
    /// Whether anything reported blocks the operation.
    /// </summary>
    /// <remarks>
    /// This, not <see cref="IsValid"/>, is what a save or publish decides on. A payload full of
    /// orphaned zones is publishable — that is the whole point of them being warnings.
    /// </remarks>
    public bool HasErrors
    {
        get
        {
            for (var i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity is ValidationSeverity.Error) return true;
            }

            return false;
        }
    }

    /// <summary>The blocking diagnostics.</summary>
    public IEnumerable<ContentValidationDiagnostic> Errors =>
        Diagnostics.Where(diagnostic => diagnostic.Severity is ValidationSeverity.Error);

    /// <summary>The non-blocking diagnostics.</summary>
    public IEnumerable<ContentValidationDiagnostic> Warnings =>
        Diagnostics.Where(diagnostic => diagnostic.Severity is ValidationSeverity.Warning);
}
