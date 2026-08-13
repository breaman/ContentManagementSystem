using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// How a structure operation ended.
/// </summary>
/// <remarks>
/// Deliberately a small closed set. The endpoint layer maps each case to one status code, so adding
/// a member is a decision about the HTTP contract rather than an implementation detail.
/// </remarks>
public enum StructureOutcome
{
    /// <summary>The operation completed.</summary>
    Success = 0,

    /// <summary>Nothing exists at the address given.</summary>
    NotFound = 1,

    /// <summary>The request was understood but breaks a rule of the content model.</summary>
    Invalid = 2,

    /// <summary>The request collides with something already stored.</summary>
    Conflict = 3,

    /// <summary>The caller may not perform this operation.</summary>
    Forbidden = 4,
}

/// <summary>
/// The outcome of a structure operation, with whatever it produced or the reasons it did not.
/// </summary>
/// <typeparam name="T">Type produced on success.</typeparam>
/// <remarks>
/// Failures are returned rather than thrown. Every one of them — a duplicate key, an immutable key,
/// a template that is not there — is an ordinary answer to an ordinary request, and exceptions would
/// turn each into a stack trace in the log of a system whose logs matter.
/// <para>
/// Diagnostics reuse <see cref="ValidationResult"/> so the API's <c>errors</c> array (spec section
/// 22.2) has one shape whether it came from a field type, the payload walk, or here.
/// </para>
/// </remarks>
public sealed class StructureResult<T>
{
    private StructureResult(StructureOutcome outcome, T? value, ValidationResult diagnostics)
    {
        Outcome = outcome;
        Value = value;
        Diagnostics = diagnostics;
    }

    /// <summary>How the operation ended.</summary>
    public StructureOutcome Outcome { get; }

    /// <summary>What the operation produced, or null when it did not succeed.</summary>
    public T? Value { get; }

    /// <summary>Why the operation ended the way it did. Empty on an uneventful success.</summary>
    public ValidationResult Diagnostics { get; }

    /// <summary>Whether the operation completed.</summary>
    public bool IsSuccess => Outcome is StructureOutcome.Success;

    /// <summary>A successful outcome.</summary>
    /// <param name="value">What the operation produced.</param>
    /// <param name="diagnostics">
    /// Non-blocking diagnostics worth reporting, such as a configuration setting accepted with a
    /// warning. Defaults to none.
    /// </param>
    public static StructureResult<T> Success(T value, ValidationResult? diagnostics = null) =>
        new(StructureOutcome.Success, value, diagnostics ?? ValidationResult.Success);

    /// <summary>Nothing exists at the address given.</summary>
    /// <param name="message">What was not found, phrased for the person who asked.</param>
    public static StructureResult<T> NotFound(string message) =>
        new(StructureOutcome.NotFound, default, ValidationResult.Error(StructureCodes.NotFound, message));

    /// <summary>The request breaks a rule of the content model.</summary>
    /// <param name="diagnostics">Every rule broken, not just the first.</param>
    public static StructureResult<T> Invalid(ValidationResult diagnostics) =>
        new(StructureOutcome.Invalid, default, diagnostics);

    /// <summary>The request breaks a single rule of the content model.</summary>
    /// <param name="code">Stable code from <see cref="StructureCodes"/>.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="path">Name of the offending member of the request, when one is to blame.</param>
    public static StructureResult<T> Invalid(string code, string message, string? path = null) =>
        new(StructureOutcome.Invalid, default, ValidationResult.Error(code, message, path));

    /// <summary>The request collides with something already stored.</summary>
    /// <param name="code">Stable code from <see cref="StructureCodes"/>.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="path">Name of the offending member of the request, when one is to blame.</param>
    public static StructureResult<T> Conflict(string code, string message, string? path = null) =>
        new(StructureOutcome.Conflict, default, ValidationResult.Error(code, message, path));

    /// <summary>The caller may not perform this operation.</summary>
    /// <param name="message">What was refused. Never names what the caller would need to hold.</param>
    public static StructureResult<T> Forbidden(string message) =>
        new(StructureOutcome.Forbidden, default, ValidationResult.Error(StructureCodes.Forbidden, message));
}
