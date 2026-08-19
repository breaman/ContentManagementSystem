using ContentManagementSystem.Core.Auditing;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

namespace ContentManagementSystem.Server.Api.Cms.Auditing;

/// <summary>
/// <c>/api/cms/v1/audit</c> — the audit log, filtered (task P7-20).
/// </summary>
/// <remarks>
/// Read-only, and there is deliberately no write route to pair with it: audit rows are a side effect
/// of saving, and an endpoint able to amend them would make the log evidence of nothing.
/// </remarks>
public static class AuditEndpoints
{
    /// <summary>
    /// Maps the audit endpoint into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet("/audit", ListAsync)
            .WithName("GetAuditLog")
            .WithSummary("Lists audit entries, newest first, filtered by entity, user, and date.")
            .WithTags("Audit")
            .RequireAuthorization(CmsPermissions.AuditView);

        return group;
    }

    private static async Task<IResult> ListAsync(
        IAuditQueryService audit,
        CancellationToken cancellationToken,
        string? entity = null,
        string? entityId = null,
        int? userId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? cursor = null,
        int limit = 50) =>
        (await audit.ListAsync(
            new AuditQuery(entity, entityId, userId, from, to, cursor, limit),
            cancellationToken))
        .ToHttpResult(Results.Ok);
}
