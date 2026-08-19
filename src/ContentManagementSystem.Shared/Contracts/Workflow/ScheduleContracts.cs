namespace ContentManagementSystem.Shared.Contracts.Workflow;

/// <summary>
/// A request to publish or retire a page at a stated moment (spec section 11.6).
/// </summary>
/// <param name="PublishOn">
/// When to publish, or null to cancel a pending publish. Must be in the future.
/// </param>
/// <param name="UnpublishOn">
/// When to retire the page, or null to cancel a pending retirement.
/// </param>
/// <remarks>
/// Both are <see cref="DateTimeOffset"/>, so the offset the editor was looking at travels with the
/// instant rather than being inferred at the far end. "Publish at 9am" on the morning the clocks
/// change is otherwise a support ticket, and an offset in the payload is what makes the answer
/// unambiguous (task P7-16).
/// </remarks>
public sealed record SetScheduleRequest(DateTimeOffset? PublishOn, DateTimeOffset? UnpublishOn);

/// <summary>
/// What is scheduled for one page, and what became of the last attempt.
/// </summary>
/// <param name="PageId">The page.</param>
/// <param name="PublishOn">When it is due to publish, or null.</param>
/// <param name="UnpublishOn">When it is due to be retired, or null.</param>
/// <param name="TimeZoneId">
/// The site's time zone, so the screen can present a stored UTC instant as the local time the editor
/// meant, with the offset shown.
/// </param>
/// <param name="PublishState">
/// Where the publish job stands: <c>Pending</c>, <c>Claimed</c>, <c>Completed</c>, <c>Failed</c>, or
/// <c>Cancelled</c>. Null when nothing has ever been scheduled.
/// </param>
/// <param name="UnpublishState">The same, for the retirement job.</param>
/// <param name="FailureReason">
/// Why the last attempt was refused. Present exactly when something failed, and shown rather than
/// buried in a log: a scheduled publish that silently did not happen is the failure mode spec
/// section 11.6 is written against.
/// </param>
public sealed record PageScheduleState(
    int PageId,
    DateTimeOffset? PublishOn,
    DateTimeOffset? UnpublishOn,
    string TimeZoneId,
    string? PublishState,
    string? UnpublishState,
    string? FailureReason);
