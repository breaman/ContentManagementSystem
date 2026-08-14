using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// The advisory edit locks the backoffice shows before two people type over each other
/// (task P2-15, spec section 11.8).
/// </summary>
/// <remarks>
/// <strong>Nothing here refuses anything.</strong> Acquiring a page somebody else holds succeeds and
/// reports who held it; the caller decides whether to warn, and the editor decides whether to carry
/// on. Locks that block are locks that get stuck — a closed laptop on a Friday would otherwise take
/// a page out of circulation until somebody with database access noticed (ADR 0012).
/// <para>
/// The authoritative protection is elsewhere: the <c>rowversion</c> on <c>PageVersion</c> turns two
/// simultaneous saves into a refusal for the second, whether or not either editor ever acquired a
/// lock. These rows exist purely so the second editor finds out <em>before</em> writing a paragraph
/// rather than after.
/// </para>
/// </remarks>
public interface IEditLockService
{
    /// <summary>How long a lock survives without a heartbeat (spec section 11.8).</summary>
    static readonly TimeSpan Expiry = TimeSpan.FromMinutes(2);

    /// <summary>How often the editor is expected to refresh a lock it holds.</summary>
    /// <remarks>
    /// Four times inside the expiry window, so a lock survives a couple of dropped requests before
    /// its holder is declared gone.
    /// </remarks>
    static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Takes or refreshes the lock on a page, and reports who ends up holding it.
    /// </summary>
    /// <param name="pageId">Identity of the page being opened.</param>
    /// <param name="takeOver">
    /// Whether to take a live lock away from its current holder. False leaves an existing holder in
    /// place and simply reports them, which is what opening the editor does; true is the explicit
    /// "Edit anyway" the UI offers afterwards.
    /// </param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The lock as it now stands, which may belong to somebody else.</returns>
    Task<CmsResult<EditLockState>> AcquireAsync(
        int pageId,
        bool takeOver = false,
        CancellationToken cancellationToken = default);

    /// <summary>Reports the live lock on a page, if there is one.</summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The lock, or a success carrying null when nobody holds one.</returns>
    Task<CmsResult<EditLockState?>> GetAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the caller's own lock, as closing the editor does.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>Whether a lock was released.</returns>
    /// <remarks>
    /// Releasing a lock held by somebody else does nothing and is not an error: the ordinary way to
    /// reach it is an editor closing a tab they had already been taken over from, and answering
    /// with a failure would put an alarming message in front of the wrong person.
    /// </remarks>
    Task<CmsResult<bool>> ReleaseAsync(int pageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every lock whose holder has gone quiet, as the reaper does.
    /// </summary>
    /// <param name="cancellationToken">Token observed while deleting.</param>
    /// <returns>The number of locks reaped.</returns>
    /// <remarks>
    /// Expiry is already enforced on read, so this is housekeeping rather than correctness: it keeps
    /// the table from accumulating a row per page anyone ever opened. Unauthorized on purpose — its
    /// caller is a hosted service with no principal, and there is nothing here to leak.
    /// </remarks>
    Task<int> ReapAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IEditLockService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="users">Identity of the caller, which is who a lock belongs to.</param>
/// <param name="clock">Source of the current time, so expiry is testable without waiting.</param>
/// <param name="logger">Log for take-overs, which are worth being able to explain afterwards.</param>
public sealed class EditLockService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IUserService users,
    TimeProvider clock,
    ILogger<EditLockService> logger) : IEditLockService
{
    /// <inheritdoc />
    public async Task<CmsResult<EditLockState>> AcquireAsync(
        int pageId,
        bool takeOver = false,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<EditLockState>.Forbidden(
                "Editing pages is not permitted.",
                PageCodes.Forbidden);
        }

        if (!await context.Pages.AnyAsync(page => page.Id == pageId, cancellationToken))
        {
            return CmsResult<EditLockState>.NotFound($"No page has id {pageId}.", PageCodes.NotFound);
        }

        var now = clock.GetUtcNow();
        var me = users.UserId;
        // The holder's navigation is deliberately not loaded: a take-over reassigns UserId, and EF's
        // relationship fixup would put the old holder's key straight back from the loaded navigation.
        var existing = await context.EditLocks
            .FirstOrDefaultAsync(candidate => candidate.PageId == pageId, cancellationToken);

        if (existing is null)
        {
            existing = new EditLock { PageId = pageId, UserId = me, AcquiredOn = now, HeartbeatOn = now };
            context.EditLocks.Add(existing);
        }
        else if (existing.UserId == me)
        {
            // The heartbeat. AcquiredOn deliberately stays put, so "opened at 09:14" keeps meaning
            // what it says over a three-hour editing session.
            existing.HeartbeatOn = now;
        }
        else if (takeOver || IsExpired(existing, now))
        {
            if (takeOver && !IsExpired(existing, now))
            {
                logger.LogInformation(
                    "User {UserId} took the edit lock on page {PageId} from user {HolderId}.",
                    me,
                    pageId,
                    existing.UserId);
            }

            existing.UserId = me;
            existing.AcquiredOn = now;
            existing.HeartbeatOn = now;
        }

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<EditLockState>.Success(await ProjectAsync(existing, me, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<CmsResult<EditLockState?>> GetAsync(
        int pageId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<EditLockState?>.Forbidden("Reading pages is not permitted.", PageCodes.Forbidden);
        }

        var lockRow = await context.EditLocks
            .AsNoTracking()
            .Include(candidate => candidate.User)
            .FirstOrDefaultAsync(candidate => candidate.PageId == pageId, cancellationToken);

        // Expiry is enforced here rather than only by the reaper, so a stale row can never be shown
        // as a live one just because nothing has swept the table in the last few seconds.
        if (lockRow is null || IsExpired(lockRow, clock.GetUtcNow()))
        {
            return CmsResult<EditLockState?>.Success(null);
        }

        return CmsResult<EditLockState?>.Success(Project(lockRow, users.UserId));
    }

    /// <inheritdoc />
    public async Task<CmsResult<bool>> ReleaseAsync(int pageId, CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentEdit))
        {
            return CmsResult<bool>.Forbidden("Editing pages is not permitted.", PageCodes.Forbidden);
        }

        var me = users.UserId;
        var lockRow = await context.EditLocks
            .FirstOrDefaultAsync(candidate => candidate.PageId == pageId, cancellationToken);

        if (lockRow is null || lockRow.UserId != me) return CmsResult<bool>.Success(false);

        context.EditLocks.Remove(lockRow);
        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<int> ReapAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = clock.GetUtcNow() - IEditLockService.Expiry;

        // The same boundary IsExpired uses, inclusive on both sides. A strict comparison here would
        // leave a lock that reads as expired but never gets reaped, which is a small window and
        // exactly the kind of disagreement that turns into "the table has rows nobody can explain".
        return await context.EditLocks
            .Where(candidate => candidate.HeartbeatOn <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private static bool IsExpired(EditLock lockRow, DateTimeOffset now) =>
        now - lockRow.HeartbeatOn >= IEditLockService.Expiry;

    /// <summary>Projects a lock whose holder may have just changed, reloading the new holder's name.</summary>
    private async Task<EditLockState> ProjectAsync(
        EditLock lockRow,
        int callerId,
        CancellationToken cancellationToken)
    {
        var name = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == lockRow.UserId)
            .Select(user => user.UserName)
            .FirstOrDefaultAsync(cancellationToken);

        return new EditLockState(
            lockRow.PageId,
            lockRow.UserId,
            name,
            lockRow.AcquiredOn,
            lockRow.HeartbeatOn,
            lockRow.UserId == callerId);
    }

    private static EditLockState Project(EditLock lockRow, int callerId) =>
        new(
            lockRow.PageId,
            lockRow.UserId,
            lockRow.User?.UserName,
            lockRow.AcquiredOn,
            lockRow.HeartbeatOn,
            lockRow.UserId == callerId);
}
