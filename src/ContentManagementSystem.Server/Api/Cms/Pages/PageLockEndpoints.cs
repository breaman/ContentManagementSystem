using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Api.Cms.Pages;

/// <summary>
/// <c>/api/cms/v1/pages/{id}/lock</c> — the advisory edit lock (task P2-19).
/// </summary>
/// <remarks>
/// <strong>None of these endpoints can refuse anything on the grounds of a lock.</strong> Acquiring
/// a page somebody else holds succeeds and reports who holds it; the caller decides whether to warn
/// and the editor decides whether to carry on (ADR 0012). The authoritative defence against a lost
/// update is the <c>rowversion</c> on the draft, which works whether or not anyone acquired
/// anything — a lock that blocked would be a lock that got stuck, and a closed laptop on a Friday
/// would take a page out of circulation until somebody with database access noticed.
/// <para>
/// The read is <c>GET</c>, the acquire and heartbeat are the same <c>POST</c>, and the release is
/// <c>DELETE</c>. Refreshing through the acquire route rather than a separate heartbeat verb keeps
/// one code path warm: the editor sends the same request every thirty seconds whether it is opening
/// the page or still on it.
/// </para>
/// </remarks>
public static class PageLockEndpoints
{
    /// <summary>
    /// Maps the edit-lock endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapPageLockEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var locks = group.MapGroup($"{PageEndpoints.Prefix}/{{pageId:int}}/lock").WithTags("Pages");

        locks.MapGet("/", GetAsync)
            .WithName("GetPageLock")
            .WithSummary("Reports who has the page open, if anyone.")
            .RequireAuthorization(CmsPermissions.ContentRead);

        locks.MapPost("/", AcquireAsync)
            .WithName("AcquirePageLock")
            .WithSummary("Takes or refreshes the lock. Never blocks editing.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        locks.MapDelete("/", ReleaseAsync)
            .WithName("ReleasePageLock")
            .WithSummary("Releases the caller's own lock, as closing the editor does.")
            .RequireAuthorization(CmsPermissions.ContentEdit)
            .RequireCmsAntiforgery();

        return group;
    }

    /// <remarks>
    /// An unheld page answers <c>204</c> rather than <c>404</c>. The lock resource is not missing —
    /// the question "who has this open" has been answered, and the answer is nobody; a 404 would be
    /// indistinguishable from the page itself not existing.
    /// </remarks>
    private static async Task<IResult> GetAsync(
        int pageId,
        IEditLockService locks,
        CancellationToken cancellationToken) =>
        (await locks.GetAsync(pageId, cancellationToken))
        .ToHttpResult(state => state is null ? Results.NoContent() : Results.Ok(state));

    private static async Task<IResult> AcquireAsync(
        int pageId,
        AcquireLockRequest? request,
        IEditLockService locks,
        CancellationToken cancellationToken) =>
        (await locks.AcquireAsync(pageId, request?.TakeOver ?? false, cancellationToken))
        .ToHttpResult(value => Results.Ok(value));

    /// <remarks>
    /// Answers <c>204</c> whether or not there was a lock to release. Releasing somebody else's is
    /// not an error — the ordinary way to reach it is an editor closing a tab they had already been
    /// taken over from, and a failure would put an alarming message in front of the wrong person.
    /// </remarks>
    private static async Task<IResult> ReleaseAsync(
        int pageId,
        IEditLockService locks,
        CancellationToken cancellationToken) =>
        (await locks.ReleaseAsync(pageId, cancellationToken)).ToHttpResult(_ => Results.NoContent());
}
