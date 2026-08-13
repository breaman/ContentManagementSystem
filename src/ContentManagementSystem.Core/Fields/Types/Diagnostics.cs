using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Collects validation diagnostics without allocating for the usual case, in which there are none.
/// </summary>
/// <remarks>
/// Validation runs per property on every save and every publish, and almost every run finds nothing.
/// The list is therefore deferred until something is actually wrong.
/// <para>
/// Lives outside <see cref="FieldTypeBase"/> so the helpers shared between field types —
/// <see cref="MediaValue"/> and the like — collect into the same list as the field type that called
/// them.
/// </para>
/// </remarks>
internal static class Diagnostics
{
    /// <summary>Appends a diagnostic, allocating the list only once something is wrong.</summary>
    /// <param name="diagnostics">The list, created on first use.</param>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public static void Add(ref List<ValidationDiagnostic>? diagnostics, ValidationDiagnostic diagnostic) =>
        (diagnostics ??= []).Add(diagnostic);

    /// <summary>Appends a blocking diagnostic.</summary>
    /// <param name="diagnostics">The list, created on first use.</param>
    /// <param name="code">Stable machine-readable discriminator.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="path">Path within the validated value, or null for the value as a whole.</param>
    public static void AddError(
        ref List<ValidationDiagnostic>? diagnostics,
        string code,
        string message,
        string? path) =>
        Add(ref diagnostics, new ValidationDiagnostic(code, message, ValidationSeverity.Error, path));

    /// <summary>Turns collected diagnostics into a result.</summary>
    /// <param name="diagnostics">Diagnostics found, or null when there were none.</param>
    /// <returns>The result to return.</returns>
    public static ValidationResult Result(List<ValidationDiagnostic>? diagnostics) =>
        diagnostics is null ? ValidationResult.Success : ValidationResult.From(diagnostics);
}
