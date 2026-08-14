using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Shared.Contracts.Api;

/// <summary>
/// Projects internal validation diagnostics onto the wire shape the management API promises.
/// </summary>
/// <remarks>
/// One projection, used by the problem-details mapper for a failed write and by the services that
/// report warnings on a successful one. Two copies would be free to disagree about which member of
/// <see cref="ValidationDiagnostic"/> becomes <see cref="ApiDiagnostic.Property"/>, and a client
/// reading the wrong one silently marks the wrong control.
/// </remarks>
public static class ApiDiagnostics
{
    /// <summary>
    /// Collects the diagnostics of one severity.
    /// </summary>
    /// <param name="diagnostics">What validation reported.</param>
    /// <param name="severity">Severity to collect.</param>
    /// <returns>The matching diagnostics, in the order they were found.</returns>
    public static IReadOnlyList<ApiDiagnostic> Project(
        ValidationResult diagnostics,
        ValidationSeverity severity)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);

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
