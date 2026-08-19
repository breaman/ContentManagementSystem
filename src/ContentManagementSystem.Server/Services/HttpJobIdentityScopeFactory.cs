using System.Security.Claims;

using ContentManagementSystem.Core.Scheduling;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Authorization;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Reconstructs the editor who scheduled a job, so the job runs as them (task P7-13).
/// </summary>
/// <param name="scopes">Factory for the service scopes each job runs in.</param>
/// <param name="logger">Log for a job whose owner no longer exists.</param>
/// <remarks>
/// The scheduled-publish counterpart to <see cref="HttpBulkOperationScopeFactory"/>, and different
/// in one way that decides the whole shape: a bulk job captures a principal that is still in memory,
/// while a scheduled job has only a user id written to a row days ago. So the principal is rebuilt
/// from the identity tables — the name-identifier claim <c>HttpUserService</c> stamps audit rows
/// from, and the role claims <c>HttpCmsAuthorization</c> allows the publish with.
/// <para>
/// If the owner has been deleted or stripped of their roles, the rebuilt principal simply holds
/// nothing and the publish is refused by the ordinary service-layer check — the job fails, its owner
/// is notified, and nothing is published on the authority of an account that no longer has it. That
/// is the correct outcome and it needs no special case here.
/// </para>
/// </remarks>
public sealed class HttpJobIdentityScopeFactory(
    IServiceScopeFactory scopes,
    ILogger<HttpJobIdentityScopeFactory> logger) : IJobIdentityScopeFactory
{
    /// <inheritdoc />
    public async Task RunAsAsync(
        int userId,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = scopes.CreateAsyncScope();

        var principal = await BuildAsync(scope.ServiceProvider, userId, cancellationToken);

        // Assigned inside the scope and before the work resolves anything: IHttpContextAccessor is
        // backed by an AsyncLocal, so this flows into everything awaited below and into nothing
        // outside this method.
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

        accessor.HttpContext = new DefaultHttpContext
        {
            User = principal,
            RequestServices = scope.ServiceProvider,
        };

        try
        {
            await work(scope.ServiceProvider, cancellationToken);
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }

    private async Task<ClaimsPrincipal> BuildAsync(
        IServiceProvider services,
        int userId,
        CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        var user = await context.Users
            .AsNoTracking()
            .Where(candidate => candidate.Id == userId)
            .Select(candidate => new { candidate.Id, candidate.UserName })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            logger.LogWarning(
                "A scheduled job names user {UserId}, who no longer exists. It will run with no " +
                "permissions and be refused.",
                userId);

            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var roles = await context.UserRoles
            .Where(assignment => assignment.UserId == userId)
            .Join(context.Roles, assignment => assignment.RoleId, role => role.Id, (_, role) => role.Name)
            .Where(name => name != null)
            .ToListAsync(cancellationToken);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, user.UserName ?? $"user-{userId}"),
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role!)));

        // The same permission claims the sign-in path stamps, so a job's principal and a request's
        // principal are the same shape. Nothing server-side reads them, but a job that produced a
        // differently shaped identity would be a difference waiting to matter.
        claims.AddRange(CmsPermissionMap.PermissionsFor(roles!)
            .Select(permission => new Claim(CmsClaimTypes.Permission, permission)));

        // The authentication type is what makes IsAuthenticated true; an identity built without one
        // is anonymous however many claims it carries, and every permission check would refuse.
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "ScheduledJob", ClaimTypes.Name, ClaimTypes.Role));
    }
}
