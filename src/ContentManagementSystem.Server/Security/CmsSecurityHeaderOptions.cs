namespace ContentManagementSystem.Server.Security;

/// <summary>
/// What a deployment gets to choose about the response security headers (tasks P9-01, P9-02).
/// </summary>
/// <remarks>
/// Deliberately small, and for the reason <see cref="Core.Security.SanitizationOptions"/> is small:
/// the directives themselves are the security boundary, and a policy a deployment can rewrite from
/// configuration is not a policy. What is here is the two switches an incident needs — report-only,
/// so a policy can be measured against real traffic before it blocks anything, and off, so a
/// deployment that has found a genuine break can ship the fix rather than a rollback.
/// </remarks>
public sealed class CmsSecurityHeaderOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:SecurityHeaders";

    /// <summary>
    /// Whether the <c>Content-Security-Policy</c> header is emitted at all.
    /// </summary>
    /// <remarks>
    /// On everywhere, including Development, on purpose. A policy that only runs in production is a
    /// policy nobody has tested, and the failures it causes are the silent kind — an editor whose
    /// syntax highlighting stopped, an embed that renders an empty box. The place to find those is a
    /// developer's machine.
    /// </remarks>
    public bool ContentSecurityPolicyEnabled { get; set; } = true;

    /// <summary>
    /// Emit the policy as <c>Content-Security-Policy-Report-Only</c> instead of enforcing it.
    /// </summary>
    /// <remarks>
    /// The measuring position of spec section 20.5's "test a policy" note: violations are reported
    /// and nothing is blocked. Never a launch setting — a report-only policy stops no attack.
    /// </remarks>
    public bool ReportOnly { get; set; }

    /// <summary>
    /// Where violation reports are posted, if anywhere.
    /// </summary>
    /// <remarks>
    /// Emitted as <c>report-uri</c>. Deprecated in favour of <c>report-to</c>, and still the only one
    /// every current browser implements; when that changes this becomes two directives rather than a
    /// different one. Empty means no reporting endpoint, which is the default because there is
    /// nothing in this deployment that collects them.
    /// </remarks>
    public string? ReportUri { get; set; }
}
