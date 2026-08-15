using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core;

/// <summary>
/// How a CMS service operation ended.
/// </summary>
/// <remarks>
/// Deliberately a small closed set. The endpoint layer maps each case to one status code, so adding
/// a member is a decision about the HTTP contract rather than an implementation detail.
/// </remarks>
public enum CmsOutcome
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
/// The outcome of a CMS service operation, with whatever it produced or the reasons it did not.
/// </summary>
/// <typeparam name="T">Type produced on success.</typeparam>
/// <remarks>
/// Failures are returned rather than thrown. Every one of them — a duplicate key, an immutable key,
/// a page that is not there — is an ordinary answer to an ordinary request, and exceptions would
/// turn each into a stack trace in the log of a system whose logs matter.
/// <para>
/// One carrier for the structure services and the page services alike, which is what lets
/// <c>CmsProblems</c> hold a single outcome-to-status mapping. A second, identical result type would
/// mean a second mapping, and the two would eventually answer the same failure with different
/// status codes.
/// </para>
/// <para>
/// Diagnostics reuse <see cref="ValidationResult"/> so the API's <c>errors</c> array (spec section
/// 22.2) has one shape whether it came from a field type, the payload walk, or here.
/// </para>
/// </remarks>
public sealed class CmsResult<T>
{
    private CmsResult(CmsOutcome outcome, T? value, ValidationResult diagnostics)
    {
        Outcome = outcome;
        Value = value;
        Diagnostics = diagnostics;
    }

    /// <summary>How the operation ended.</summary>
    public CmsOutcome Outcome { get; }

    /// <summary>
    /// What the operation produced, or null when it did not succeed.
    /// </summary>
    /// <remarks>
    /// One exception, and it is deliberate: a <see cref="CmsOutcome.Conflict"/> may carry the state
    /// the caller lost to. A draft save that lost a race has to hand back what is stored so the
    /// editor can offer "keep mine / take theirs / open diff" (spec section 11.8), and a second
    /// round trip to fetch it would race the same way.
    /// </remarks>
    public T? Value { get; }

    /// <summary>Why the operation ended the way it did. Empty on an uneventful success.</summary>
    public ValidationResult Diagnostics { get; }

    /// <summary>Whether the operation completed.</summary>
    public bool IsSuccess => Outcome is CmsOutcome.Success;

    /// <summary>A successful outcome.</summary>
    /// <param name="value">What the operation produced.</param>
    /// <param name="diagnostics">
    /// Non-blocking diagnostics worth reporting, such as a configuration setting accepted with a
    /// warning. Defaults to none.
    /// </param>
    public static CmsResult<T> Success(T value, ValidationResult? diagnostics = null) =>
        new(CmsOutcome.Success, value, diagnostics ?? ValidationResult.Success);

    /// <summary>Nothing exists at the address given.</summary>
    /// <param name="message">What was not found, phrased for the person who asked.</param>
    /// <param name="code">
    /// Stable code for the surface reporting it. Defaults to the structure vocabulary because that
    /// is where this carrier shipped first and a code does not change once shipped; every other
    /// surface passes its own — <c>PageService</c> passes <see cref="PageCodes.NotFound"/>.
    /// </param>
    public static CmsResult<T> NotFound(string message, string code = StructureCodes.NotFound) =>
        new(CmsOutcome.NotFound, default, ValidationResult.Error(code, message));

    /// <summary>The request breaks a rule of the content model.</summary>
    /// <param name="diagnostics">Every rule broken, not just the first.</param>
    public static CmsResult<T> Invalid(ValidationResult diagnostics) =>
        new(CmsOutcome.Invalid, default, diagnostics);

    /// <summary>The request breaks a single rule of the content model.</summary>
    /// <param name="code">Stable code from <see cref="StructureCodes"/> or <see cref="PageCodes"/>.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="path">Name of the offending member of the request, when one is to blame.</param>
    public static CmsResult<T> Invalid(string code, string message, string? path = null) =>
        new(CmsOutcome.Invalid, default, ValidationResult.Error(code, message, path));

    /// <summary>The request collides with something already stored.</summary>
    /// <param name="code">Stable code from <see cref="StructureCodes"/> or <see cref="PageCodes"/>.</param>
    /// <param name="message">Human-readable explanation.</param>
    /// <param name="path">Name of the offending member of the request, when one is to blame.</param>
    /// <param name="value">
    /// The state that blocked the request, when the caller needs it to resolve the conflict. See
    /// <see cref="Value"/>.
    /// </param>
    public static CmsResult<T> Conflict(string code, string message, string? path = null, T? value = default) =>
        new(CmsOutcome.Conflict, value, ValidationResult.Error(code, message, path));

    /// <summary>The request collides with several things already stored.</summary>
    /// <param name="diagnostics">Every collision, not just the first.</param>
    /// <remarks>
    /// A subtree rebuild can find more than one taken URL in a single operation, and reporting them
    /// one at a time would make an editor fix a five-page collision in five round trips.
    /// </remarks>
    public static CmsResult<T> Conflict(ValidationResult diagnostics) =>
        new(CmsOutcome.Conflict, default, diagnostics);

    /// <summary>The caller may not perform this operation.</summary>
    /// <param name="message">What was refused. Never names what the caller would need to hold.</param>
    /// <param name="code">Stable code for the surface reporting it. See <see cref="NotFound"/>.</param>
    public static CmsResult<T> Forbidden(string message, string code = StructureCodes.Forbidden) =>
        new(CmsOutcome.Forbidden, default, ValidationResult.Error(code, message));
}
