using ContentManagementSystem.Shared.Contracts.Appearance;

namespace ContentManagementSystem.Core.Appearance;

/// <summary>
/// Decides whether a site stylesheet may be stored and served (spec section 30.5).
/// </summary>
/// <remarks>
/// One validator, run on save, on publish, and on the preview render — the same arrangement HTML
/// gets (D8), and for the same reason: a check that only runs at one end is a check an import, a
/// direct database write, or a later code path walks straight past.
/// <para>
/// Unlike the HTML sanitizer, this one never edits what it was given. It reports, and its caller
/// refuses the whole save. Silently deleting a rule an administrator typed produces a stylesheet
/// that does not match what they wrote and a bug they cannot reproduce (D27).
/// </para>
/// </remarks>
public interface ICssValidator
{
    /// <summary>
    /// Finds every refused construct in a stylesheet.
    /// </summary>
    /// <param name="css">The stylesheet source. Null or empty is valid — it publishes nothing.</param>
    /// <returns>
    /// Every diagnostic, in the order the constructs appear. Empty means the stylesheet may be
    /// stored and served.
    /// </returns>
    /// <remarks>
    /// Every problem is reported rather than only the first. An administrator fixing one
    /// <c>@import</c> per save round trip would rewrite a pasted stylesheet a line at a time.
    /// </remarks>
    IReadOnlyList<CssDiagnostic> Validate(string? css);
}
