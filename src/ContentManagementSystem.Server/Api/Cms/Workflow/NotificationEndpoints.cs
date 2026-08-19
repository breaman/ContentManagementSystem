using ContentManagementSystem.Core.Notifications;

namespace ContentManagementSystem.Server.Api.Cms.Workflow;

/// <summary>
/// <c>/api/cms/v1/notifications</c> — the signed-in editor's own inbox (task P7-19,
/// spec section 14.8).
/// </summary>
/// <remarks>
/// No permission policy beyond the group's authentication floor, and none is needed: every query
/// here is scoped to the caller inside the service, so the only inbox anybody can reach is their
/// own. A permission would be a second, weaker statement of the same thing.
/// </remarks>
public static class NotificationEndpoints
{
    /// <summary>
    /// Maps the inbox endpoints into the versioned CMS API group.
    /// </summary>
    /// <param name="group">The <c>/api/cms/v1</c> group.</param>
    /// <returns>The group, for chaining.</returns>
    public static RouteGroupBuilder MapNotificationEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        var inbox = group.MapGroup("/notifications").WithTags("Workflow");

        inbox.MapGet("/", InboxAsync)
            .WithName("GetNotifications")
            .WithSummary("Reads the caller's own inbox and unread count.");

        inbox.MapPost("/read", MarkReadAsync)
            .WithName("MarkNotificationsRead")
            .WithSummary("Marks one notification read, or every unread one.")
            .RequireCmsAntiforgery();

        return group;
    }

    private static async Task<IResult> InboxAsync(
        INotificationService notifications,
        CancellationToken cancellationToken,
        bool unreadOnly = false,
        int limit = 50) =>
        (await notifications.InboxAsync(unreadOnly, limit, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> MarkReadAsync(
        INotificationService notifications,
        CancellationToken cancellationToken,
        int? id = null) =>
        (await notifications.MarkReadAsync(id, cancellationToken)).ToHttpResult(Results.Ok);
}
