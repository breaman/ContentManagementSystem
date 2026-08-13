namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// Sanitized markup together with an account of what the profile took out of it.
/// </summary>
/// <param name="Html">The markup that is safe to store and to emit.</param>
/// <param name="Removals">Everything removed, in document order.</param>
public sealed record SanitizationResult(string Html, IReadOnlyList<SanitizationRemoval> Removals)
{
    /// <summary>A result for markup that had nothing removed.</summary>
    /// <param name="html">The markup.</param>
    public static SanitizationResult Unchanged(string html) => new(html, []);

    /// <summary>Whether the profile removed anything at all.</summary>
    public bool RemovedAnything => Removals.Count > 0;
}
