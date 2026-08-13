using System.Collections.ObjectModel;

namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// How strictly a payload is being checked.
/// </summary>
/// <remarks>
/// A first-class parameter of validation rather than a filter applied to the results afterwards.
/// Filtering after the fact reads the same until someone needs to know <em>why</em> a draft saved
/// but a publish did not, and by then the reason has been discarded.
/// </remarks>
public enum ValidationMode
{
    /// <summary>
    /// Saving a draft. Structural problems are errors; an unfilled required value is not, because
    /// an editor must always be able to save half-finished work (spec section 8.3).
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Publishing. Everything <see cref="Draft"/> rejects, plus required values that are empty.
    /// </summary>
    Publish = 1,
}

/// <summary>
/// Whether a diagnostic blocks the operation or merely reports on it.
/// </summary>
public enum ValidationSeverity
{
    /// <summary>
    /// Reported but not blocking. Orphaned zones and properties land here, which is what makes the
    /// "removing a zone keeps its data" rule implementable rather than aspirational
    /// (spec section 8.5).
    /// </summary>
    Warning = 0,

    /// <summary>Blocks the save or publish that produced it.</summary>
    Error = 1,
}

/// <summary>
/// One problem found in a content value.
/// </summary>
/// <param name="Code">
/// Stable machine-readable discriminator, such as <c>field.maxLength</c>. Clients switch on this;
/// the message is for humans and may be reworded freely.
/// </param>
/// <param name="Message">Human-readable explanation, phrased for a content editor.</param>
/// <param name="Severity">Whether this blocks the operation.</param>
/// <param name="RelativePath">
/// Path from the value being validated down to the offending part, for field types that validate
/// structured values. Null when the diagnostic concerns the value as a whole. The schema walk
/// prefixes the absolute payload path, since a field type cannot know where in the document it sits.
/// </param>
public readonly record struct ValidationDiagnostic(
    string Code,
    string Message,
    ValidationSeverity Severity = ValidationSeverity.Error,
    string? RelativePath = null);

/// <summary>
/// The outcome of validating one content value.
/// </summary>
/// <example>
/// <code>
/// if (value.ValueKind is not JsonValueKind.String)
/// {
///     return ValidationResult.Error("field.type", "Expected a text value.");
/// }
///
/// return ValidationResult.Success;
/// </code>
/// </example>
public sealed class ValidationResult
{
    private static readonly ReadOnlyCollection<ValidationDiagnostic> NoDiagnostics =
        new List<ValidationDiagnostic>().AsReadOnly();

    /// <summary>A result with nothing to report. Allocation-free, for the common case.</summary>
    public static ValidationResult Success { get; } = new(NoDiagnostics);

    private ValidationResult(IReadOnlyList<ValidationDiagnostic> diagnostics) => Diagnostics = diagnostics;

    /// <summary>Everything found, in the order it was found.</summary>
    public IReadOnlyList<ValidationDiagnostic> Diagnostics { get; }

    /// <summary>Whether anything at all was reported.</summary>
    public bool IsValid => Diagnostics.Count == 0;

    /// <summary>Whether anything reported blocks the operation.</summary>
    public bool HasErrors
    {
        get
        {
            // Indexed rather than LINQ: validation runs per property on every save and publish.
            for (var i = 0; i < Diagnostics.Count; i++)
            {
                if (Diagnostics[i].Severity is ValidationSeverity.Error) return true;
            }

            return false;
        }
    }

    /// <summary>Creates a result carrying a single blocking diagnostic.</summary>
    /// <param name="code">Stable machine-readable discriminator.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="relativePath">Optional path within the validated value.</param>
    public static ValidationResult Error(string code, string message, string? relativePath = null) =>
        From(new ValidationDiagnostic(code, message, ValidationSeverity.Error, relativePath));

    /// <summary>Creates a result carrying a single non-blocking diagnostic.</summary>
    /// <param name="code">Stable machine-readable discriminator.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="relativePath">Optional path within the validated value.</param>
    public static ValidationResult Warning(string code, string message, string? relativePath = null) =>
        From(new ValidationDiagnostic(code, message, ValidationSeverity.Warning, relativePath));

    /// <summary>Creates a result from one diagnostic.</summary>
    /// <param name="diagnostic">The diagnostic to report.</param>
    public static ValidationResult From(ValidationDiagnostic diagnostic) =>
        new(new List<ValidationDiagnostic>(1) { diagnostic }.AsReadOnly());

    /// <summary>Creates a result from several diagnostics, or <see cref="Success"/> if there are none.</summary>
    /// <param name="diagnostics">The diagnostics to report.</param>
    public static ValidationResult From(IEnumerable<ValidationDiagnostic> diagnostics)
    {
        var collected = new List<ValidationDiagnostic>(diagnostics);

        return collected.Count == 0 ? Success : new ValidationResult(collected.AsReadOnly());
    }
}
