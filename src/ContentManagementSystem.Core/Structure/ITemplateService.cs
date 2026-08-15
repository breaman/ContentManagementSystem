using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// Reads and writes template definitions on behalf of the structure API (task P1-21).
/// </summary>
/// <remarks>
/// Every method authorizes the caller itself. The endpoints carry a policy as well, but the policy
/// is the door and this is the lock — a service reached from a CLI verb or a hosted job is subject
/// to the same rules (spec section 20.4).
/// <para>
/// The delete arrived with <c>P1-32</c>, once <c>Page</c> existed for its guard to ask. It was
/// deliberately not shipped before that: a delete that cannot enforce its own rule is a hole with a
/// date on it.
/// </para>
/// </remarks>
public interface ITemplateService
{
    /// <summary>Lists every template, in the order the create-page picker shows them.</summary>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The templates, or a forbidden result when the caller may not read content.</returns>
    Task<CmsResult<IReadOnlyList<TemplateSummary>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one template with its current zone definitions.</summary>
    /// <param name="id">Identity of the template.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The template, or a not-found result.</returns>
    Task<CmsResult<TemplateDetail>> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a template and cuts its first revision.
    /// </summary>
    /// <param name="request">The template to create.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// The created template, an invalid result naming every rule the request broke, or a conflict
    /// when the key is taken.
    /// </returns>
    Task<CmsResult<TemplateDetail>> CreateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a template's editor-facing metadata.
    /// </summary>
    /// <param name="id">Identity of the template.</param>
    /// <param name="request">The new values. A changed key is refused.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The updated template, a not-found result, or an invalid result.</returns>
    /// <remarks>
    /// Cuts no revision. A revision captures the <em>zone set</em>; a renamed display name changes
    /// nothing about how stored content is read, and cutting one for it would bury the structural
    /// history that revisions exist to record.
    /// </remarks>
    Task<CmsResult<TemplateDetail>> UpdateAsync(
        int id,
        UpdateTemplateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a template, its zone definitions, and its revision history.
    /// </summary>
    /// <param name="id">Identity of the template.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>
    /// Success, a not-found result, or a conflict naming the pages that stopped it (task P1-32,
    /// spec section 8.5).
    /// </returns>
    /// <remarks>
    /// <strong>Refused while any page references the template, including one in the recycle bin.</strong>
    /// Spec section 8.5 words the rule as non-deleted pages, and that reading is a page that cannot
    /// be restored: a recycled page keeps its <c>TemplateId</c>, so deleting its template turns a
    /// restore into an unrenderable page whose schema no longer exists — and the foreign key is
    /// <c>Restrict</c> in both directions anyway, so the narrower guard would hand the caller a
    /// database error instead of an answer. The two cases are counted separately in the refusal,
    /// because emptying the recycle bin is a remedy the caller can act on.
    /// <para>
    /// A hard delete, like a composition's. Templates carry no soft-delete flag, and once nothing
    /// references one there is no content whose schema its revisions still pin.
    /// </para>
    /// </remarks>
    Task<CmsResult<bool>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Lists a template's structural revisions, newest first.</summary>
    /// <param name="id">Identity of the template.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The revision history, or a not-found result.</returns>
    Task<CmsResult<IReadOnlyList<TemplateRevisionSummary>>> ListRevisionsAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>Reads one revision and the zone definitions it captured.</summary>
    /// <param name="id">Identity of the template.</param>
    /// <param name="revisionNumber">The revision number, as a page version records it.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The revision, or a not-found result.</returns>
    Task<CmsResult<TemplateRevisionDetail>> GetRevisionAsync(
        int id,
        int revisionNumber,
        CancellationToken cancellationToken = default);
}
