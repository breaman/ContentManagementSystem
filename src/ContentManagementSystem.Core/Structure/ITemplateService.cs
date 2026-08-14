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
/// Nothing here deletes a template. Deletion is blocked while any non-deleted page references the
/// template (spec section 8.5), and there is no page table to ask until Phase 2; shipping a delete
/// that cannot yet enforce its own guard would be a hole with a date on it. It arrives with
/// <c>P1-32</c>'s remaining rules once <c>Page</c> exists.
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
