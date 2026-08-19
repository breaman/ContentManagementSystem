using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Workflow;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Notifications;

/// <inheritdoc cref="INotificationService" />
/// <param name="context">The application database context.</param>
/// <param name="email">The mail transport, which is allowed to fail.</param>
/// <param name="options">Where the backoffice lives, so a link in an email is followable.</param>
/// <param name="users">Who the caller is, so nobody is told about their own action.</param>
/// <param name="clock">Source of the current time.</param>
/// <param name="logger">Log for mail that could not be sent.</param>
public sealed class NotificationService(
    ApplicationDbContext context,
    ICmsEmailSender email,
    IOptions<CmsEmailOptions> options,
    IUserService users,
    TimeProvider clock,
    ILogger<NotificationService> logger) : INotificationService
{
    /// <summary>Largest inbox page, whatever was asked for.</summary>
    private const int MaxLimit = 200;

    /// <inheritdoc />
    public Task NotifyAsync(
        int userId,
        NotificationKind kind,
        int? pageId,
        string pageTitle,
        string? actor = null,
        string? note = null,
        string? link = null,
        bool includeCaller = false,
        CancellationToken cancellationToken = default) =>
        NotifyManyAsync(
            [userId], kind, pageId, pageTitle, actor, note, link, includeCaller, cancellationToken);

    /// <inheritdoc />
    public async Task NotifyManyAsync(
        IEnumerable<int> userIds,
        NotificationKind kind,
        int? pageId,
        string pageTitle,
        string? actor = null,
        string? note = null,
        string? link = null,
        bool includeCaller = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userIds);

        var me = includeCaller ? 0 : users.UserId;

        // Distinct, and by default never the person who caused it. Both rules live here rather than
        // at the call sites: an approver who is also the assignee would otherwise get two copies of
        // their own decision, and every caller would have to remember the same two lines. See
        // INotificationService for why the scheduler opts out of the second rule.
        var recipients = userIds.Distinct().Where(id => id > 0 && id != me).ToList();

        if (recipients.Count == 0) return;

        var rendered = NotificationTemplates.Render(kind, pageTitle, actor, note);
        var now = clock.GetUtcNow();

        foreach (var recipient in recipients)
        {
            context.Notifications.Add(new Notification
            {
                UserId = recipient,
                Kind = kind,
                Subject = Trim(rendered.Subject, FieldLengths.ContentTitle),
                Body = Trim(rendered.Body, FieldLengths.CommentBody),
                PageId = pageId,
                Link = link,
                CreatedOn = now,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        await SendMailAsync(recipients, rendered, link, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<CmsResult<NotificationInbox>> InboxAsync(
        bool unreadOnly = false,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var me = users.UserId;

        if (me <= 0)
        {
            return CmsResult<NotificationInbox>.Forbidden(
                "There is nobody signed in to have an inbox.",
                WorkflowCodes.Forbidden);
        }

        var mine = context.Notifications.AsNoTracking().Where(row => row.UserId == me);

        var unread = await mine.CountAsync(row => row.ReadOn == null, cancellationToken);

        var rows = await (unreadOnly ? mine.Where(row => row.ReadOn == null) : mine)
            .OrderByDescending(row => row.Id)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .Select(row => new NotificationSummary(
                row.Id,
                row.Kind.ToString(),
                row.Subject,
                row.Body,
                row.PageId,
                row.Link,
                row.CreatedOn,
                row.ReadOn))
            .ToListAsync(cancellationToken);

        return CmsResult<NotificationInbox>.Success(new NotificationInbox(rows, unread));
    }

    /// <inheritdoc />
    public async Task<CmsResult<int>> MarkReadAsync(
        int? notificationId,
        CancellationToken cancellationToken = default)
    {
        var me = users.UserId;

        if (me <= 0)
        {
            return CmsResult<int>.Forbidden(
                "There is nobody signed in to have an inbox.",
                WorkflowCodes.Forbidden);
        }

        var now = clock.GetUtcNow();

        // The ownership predicate is part of the update rather than a check after a load, so a
        // guessed id cannot mark somebody else's notification read — it simply matches no rows.
        var marked = await context.Notifications
            .Where(row => row.UserId == me && row.ReadOn == null)
            .Where(row => notificationId == null || row.Id == notificationId)
            .ExecuteUpdateAsync(row => row.SetProperty(item => item.ReadOn, now), cancellationToken);

        return CmsResult<int>.Success(marked);
    }

    /// <summary>Sends the mail half, and never lets its failure reach the caller.</summary>
    private async Task SendMailAsync(
        IReadOnlyList<int> recipients,
        NotificationTemplates.Rendered rendered,
        string? link,
        CancellationToken cancellationToken)
    {
        var addresses = await context.Users
            .AsNoTracking()
            .Where(user => recipients.Contains(user.Id) && user.Email != null)
            .Select(user => user.Email!)
            .ToListAsync(cancellationToken);

        if (addresses.Count == 0) return;

        var body = Absolute(link) is { } url ? $"{rendered.Body}\n\n{url}" : rendered.Body;

        foreach (var address in addresses)
        {
            try
            {
                await email.SendAsync(address, rendered.Subject, body, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Belt and braces: the contract says a sender does not throw for a delivery
                // failure, and this is what happens when one does anyway. The inbox row is already
                // committed, so the editor is still told.
                logger.LogError(
                    exception,
                    "A notification email to {Address} could not be sent. The in-app notification stands.",
                    address);
            }
        }
    }

    /// <summary>Turns a backoffice path into a URL a mail client can follow.</summary>
    private string? Absolute(string? link)
    {
        if (string.IsNullOrWhiteSpace(link)) return null;

        var baseUrl = options.Value.SiteBaseUrl;

        return string.IsNullOrWhiteSpace(baseUrl) ? null : $"{baseUrl.TrimEnd('/')}{link}";
    }

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
