using ContentManagementSystem.Shared.Contracts.Api;

namespace ContentManagementSystem.Shared.Contracts.Structure;

/// <summary>
/// What a structure write produced, as the backoffice screens need to see it.
/// </summary>
/// <typeparam name="T">Type the operation produces on success.</typeparam>
/// <param name="Value">What was produced, or null when the write did not succeed.</param>
/// <param name="Errors">Everything that blocked the write. Empty on success.</param>
/// <param name="Warnings">
/// Everything worth saying about a write that <em>did</em> succeed, such as a configuration setting
/// whose phase has not shipped. Empty is the ordinary case.
/// </param>
/// <remarks>
/// Deliberately not <c>StructureResult&lt;T&gt;</c>: that type lives in <c>Core</c>, which the
/// WebAssembly backoffice cannot reference, and it carries an outcome enum whose only consumer is
/// the HTTP status mapping. What a screen needs is narrower — did it work, and what should I show
/// the developer — and this is that, in a project both implementations can see.
/// </remarks>
public sealed record StructureClientResult<T>(
    T? Value,
    IReadOnlyList<ApiDiagnostic> Errors,
    IReadOnlyList<ApiDiagnostic> Warnings)
{
    /// <summary>Whether the write succeeded.</summary>
    public bool IsSuccess => Value is not null && Errors.Count == 0;

    /// <summary>A successful write.</summary>
    /// <param name="value">What it produced.</param>
    /// <param name="warnings">Anything non-blocking worth reporting.</param>
    public static StructureClientResult<T> Success(T value, IReadOnlyList<ApiDiagnostic>? warnings = null) =>
        new(value, [], warnings ?? []);

    /// <summary>A write that did not happen.</summary>
    /// <param name="errors">Everything that blocked it.</param>
    /// <param name="warnings">Anything non-blocking reported alongside.</param>
    public static StructureClientResult<T> Failure(
        IReadOnlyList<ApiDiagnostic> errors,
        IReadOnlyList<ApiDiagnostic>? warnings = null) =>
        new(default, errors, warnings ?? []);

    /// <summary>A write that failed for one reason, phrased for a developer.</summary>
    /// <param name="code">Stable code, from <see cref="StructureCodes"/> where one fits.</param>
    /// <param name="message">What went wrong.</param>
    public static StructureClientResult<T> Failure(string code, string message) =>
        new(default, [new ApiDiagnostic(code, message)], []);
}

/// <summary>
/// The RFC 9457 body the management API returns, as a client deserializes it (spec section 22.2).
/// </summary>
/// <param name="Title">Short, stable summary of the problem class.</param>
/// <param name="Detail">Human-readable explanation.</param>
/// <param name="Status">The HTTP status.</param>
/// <param name="Errors">Everything that blocked the request.</param>
/// <param name="Warnings">Anything non-blocking reported alongside.</param>
public sealed record ProblemResponse(
    string? Title,
    string? Detail,
    int? Status,
    IReadOnlyList<ApiDiagnostic>? Errors,
    IReadOnlyList<ApiDiagnostic>? Warnings);
