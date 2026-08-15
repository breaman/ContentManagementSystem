namespace ContentManagementSystem.Shared.Contracts.Content;

/// <summary>
/// Body of <c>POST /api/cms/v1/pages/{id}/lock</c>.
/// </summary>
/// <param name="TakeOver">
/// Whether to take a live lock away from its current holder. False — the default, and what opening
/// the editor sends — leaves an existing holder in place and simply reports them; true is the
/// explicit "Edit anyway" the UI offers afterwards.
/// </param>
/// <remarks>
/// Both forms succeed. Neither can fail for the reason a caller might expect, because a lock never
/// prevents editing (ADR 0012) — the difference is only whose name the banner shows next.
/// </remarks>
public sealed record AcquireLockRequest(bool TakeOver = false);

/// <summary>
/// Who has a page open in the editor, as the backoffice shows it (spec section 11.8).
/// </summary>
/// <param name="PageId">Page the lock is on.</param>
/// <param name="UserId">Editor holding it.</param>
/// <param name="UserName">That editor's display name, so the banner can say who rather than which id.</param>
/// <param name="AcquiredOn">When they opened the page.</param>
/// <param name="HeartbeatOn">Last sign of life, which is what "last active 12 s ago" is computed from.</param>
/// <param name="IsMine">Whether the caller is the holder, in which case there is nothing to warn about.</param>
/// <remarks>
/// <strong>A lock never prevents editing.</strong> This is advisory information the UI shows before
/// somebody starts typing over a colleague; the authoritative defence against a lost update is the
/// <c>rowversion</c> on the draft, which works whether or not anyone acquired anything (ADR 0012).
/// That is also why an expired lock is simply absent here rather than reported as a distinct state —
/// there is no third thing for a caller to do about it.
/// </remarks>
public sealed record EditLockState(
    int PageId,
    int UserId,
    string? UserName,
    DateTimeOffset AcquiredOn,
    DateTimeOffset HeartbeatOn,
    bool IsMine);
