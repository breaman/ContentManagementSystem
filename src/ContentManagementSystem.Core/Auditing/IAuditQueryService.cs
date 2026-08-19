using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Core.Auditing;

/// <summary>
/// Reads the audit log (task P7-20, spec section 21.1).
/// </summary>
/// <remarks>
/// Read-only by construction: there is no write path here, and there is not meant to be. Audit rows
/// are written by <c>AuditLogInterceptor</c> as a side effect of saving, and a service able to amend
/// them would make the log evidence of nothing.
/// <para>
/// Guarded by <c>Audit.View</c>, which section 21.1 gives to administrators and developers.
/// Diagnosing "what happened to this page" is development work, and requiring the ability to grant
/// yourself roles in order to do it would be the wrong trade.
/// </para>
/// </remarks>
public interface IAuditQueryService
{
    /// <summary>Lists audit entries, newest first.</summary>
    /// <param name="query">Which entries.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>A page of entries and the cursor for the next.</returns>
    /// <remarks>
    /// Newest first, because every question anybody brings to an audit log — "who unpublished the
    /// homepage and when" — is a question about the recent past (criterion P7 #10).
    /// </remarks>
    Task<CmsResult<CursorPage<AuditEntrySummary>>> ListAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default);
}
