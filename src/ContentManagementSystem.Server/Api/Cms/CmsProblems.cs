using System.Diagnostics;
using System.Net;

using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Mvc;

namespace ContentManagementSystem.Server.Api.Cms;

/// <summary>
/// Turns a service outcome into the RFC 9457 response the management API promises
/// (spec section 22.2).
/// </summary>
/// <remarks>
/// Kept in one place so every endpoint answers the same failure with the same status code and the
/// same body. The mapping from outcome to status is the API's contract, and scattering it across
/// handlers is how a 404 becomes a 400 in one corner of the surface.
/// </remarks>
public static class CmsProblems
{
    /// <summary>Base for the problem type URIs this API returns.</summary>
    private const string TypeBase = "https://cms.example/errors/";

    /// <summary>
    /// Maps a structure result to an HTTP response.
    /// </summary>
    /// <typeparam name="T">Type the operation produced on success.</typeparam>
    /// <param name="result">The outcome to map.</param>
    /// <param name="onSuccess">Builds the response for a successful outcome.</param>
    /// <returns>The response.</returns>
    public static IResult ToHttpResult<T>(this StructureResult<T> result, Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess) return onSuccess(result.Value!);

        return result.Outcome switch
        {
            StructureOutcome.NotFound => Problem(
                HttpStatusCode.NotFound, "not-found", "Not found", result.Diagnostics),
            StructureOutcome.Forbidden => Problem(
                HttpStatusCode.Forbidden, "forbidden", "Forbidden", result.Diagnostics),
            StructureOutcome.Conflict => Problem(
                HttpStatusCode.Conflict, "conflict", "Conflict", result.Diagnostics),
            // 422 rather than 400: the request parsed and bound, and what it asked for is
            // understood — it simply breaks a rule of the content model.
            StructureOutcome.Invalid => Problem(
                HttpStatusCode.UnprocessableEntity, "validation", "Validation failed", result.Diagnostics),
            _ => throw new UnreachableException($"Unmapped structure outcome '{result.Outcome}'."),
        };
    }

    /// <summary>
    /// Builds a problem response carrying the <c>errors</c> and <c>warnings</c> arrays.
    /// </summary>
    /// <param name="status">Status code to return.</param>
    /// <param name="type">Last segment of the problem type URI.</param>
    /// <param name="title">Short, stable summary of the problem class.</param>
    /// <param name="diagnostics">What went wrong.</param>
    /// <returns>The response.</returns>
    public static IResult Problem(
        HttpStatusCode status,
        string type,
        string title,
        ValidationResult diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

        var errors = Project(diagnostics, ValidationSeverity.Error);
        var warnings = Project(diagnostics, ValidationSeverity.Warning);

        var problem = new ProblemDetails
        {
            Type = TypeBase + type,
            Title = title,
            Status = (int)status,
            Detail = errors.Count switch
            {
                0 => title,
                1 => errors[0].Message,
                _ => $"{errors.Count} problems prevent this change.",
            },
        };

        // Always present, even when empty, so a client can read response.errors without first
        // checking whether the member exists.
        problem.Extensions["errors"] = errors;
        problem.Extensions["warnings"] = warnings;

        return Results.Problem(problem);
    }

    private static List<ApiDiagnostic> Project(ValidationResult diagnostics, ValidationSeverity severity)
    {
        var projected = new List<ApiDiagnostic>();

        foreach (var diagnostic in diagnostics.Diagnostics)
        {
            if (diagnostic.Severity == severity)
            {
                projected.Add(new ApiDiagnostic(diagnostic.Code, diagnostic.Message, diagnostic.RelativePath));
            }
        }

        return projected;
    }
}
