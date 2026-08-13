using System.Diagnostics.CodeAnalysis;

namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// Resolves the captured schema a stored payload names.
/// </summary>
/// <remarks>
/// Lookups are by <em>key and revision</em>, never by key alone, because that is how content
/// addresses its schema: a page version renders and validates against the revision it captured, not
/// against whatever the template looks like today (spec section 8.5).
/// <para>
/// Every lookup can fail, and none of them throws. A revision can be missing legitimately — a
/// template deleted, an environment promoted out of order, a payload restored from a backup older
/// than the structure it names — and the walk reports that as a diagnostic rather than as an
/// exception on a save or, worse, on a public request (spec section 15.3).
/// </para>
/// </remarks>
public interface IContentSchemaCatalog
{
    /// <summary>Finds a template revision's zone definitions.</summary>
    /// <param name="templateKey">Key of the template.</param>
    /// <param name="revisionNumber">The revision the payload captured.</param>
    /// <param name="schema">The schema when the revision is known.</param>
    /// <returns><see langword="true"/> when the revision is known.</returns>
    bool TryGetTemplate(string templateKey, int revisionNumber, [NotNullWhen(true)] out ContentSchema? schema);

    /// <summary>Finds a block type revision's property definitions.</summary>
    /// <param name="blockTypeKey">Key of the block type.</param>
    /// <param name="revisionNumber">
    /// The revision the block captured, or null when it carries none — a payload written before
    /// revisions were captured, which falls back to the newest known revision rather than being
    /// treated as unknown.
    /// </param>
    /// <param name="schema">The schema when the revision is known.</param>
    /// <returns><see langword="true"/> when the revision is known.</returns>
    bool TryGetBlockType(
        string blockTypeKey,
        int? revisionNumber,
        [NotNullWhen(true)] out BlockTypeSchema? schema);
}
