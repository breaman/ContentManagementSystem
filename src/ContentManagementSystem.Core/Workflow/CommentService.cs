using System.Text.RegularExpressions;

using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Workflow;

/// <inheritdoc cref="ICommentService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
/// <param name="acl">Where in the tree the caller may do it (spec section 21.2).</param>
/// <param name="users">Who the caller is, which is who a remark is attributed to.</param>
/// <param name="notifications">Tells anybody named in a remark that they were named.</param>
/// <param name="clock">Source of the current time.</param>
public sealed partial class CommentService(
    ApplicationDbContext context,
    ICmsAuthorization authorization,
    IAclService acl,
    IUserService users,
    INotificationService notifications,
    TimeProvider clock) : ICommentService
{
    /// <inheritdoc />
    public async Task<CmsResult<IReadOnlyList<CommentSummary>>> ListAsync(
        int pageId,
        string? zoneKey = null,
        bool includeResolved = true,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<IReadOnlyList<CommentSummary>>.Forbidden(
                "Reading pages is not permitted.",
                WorkflowCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return CmsResult<IReadOnlyList<CommentSummary>>.NotFound(
                $"No page has id {pageId}.",
                WorkflowCodes.NotFound);
        }

        var rows = await context.Comments
            .AsNoTracking()
            .Where(comment => comment.PageId == pageId)
            .Where(comment => zoneKey == null || comment.ZoneKey == zoneKey)
            .OrderBy(comment => comment.Id)
            .Select(comment => new CommentRow(
                comment.Id,
                comment.ParentCommentId,
                comment.PageVersionId,
                comment.ZoneKey,
                comment.Body,
                comment.CreatedBy,
                context.Users.Where(user => user.Id == comment.CreatedBy)
                    .Select(user => user.UserName).FirstOrDefault(),
                comment.CreatedOn,
                comment.ResolvedOn,
                comment.ResolvedBy != null ? comment.ResolvedBy.UserName : null))
            .ToListAsync(cancellationToken);

        return CmsResult<IReadOnlyList<CommentSummary>>.Success(
            Assemble(pageId, rows, includeResolved));
    }

    /// <inheritdoc />
    public async Task<CmsResult<CommentSummary>> AddAsync(
        int pageId,
        CreateCommentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Commenting needs read, not edit. Half the point of review comments is that somebody who
        // may not touch the content can still say what is wrong with it — an Approver's editing is
        // confined to what is assigned to them, and a Viewer may not edit at all.
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<CommentSummary>.Forbidden(
                "Reading pages is not permitted.",
                WorkflowCodes.Forbidden);
        }

        if (!await acl.IsAllowedAsync(CmsPermissions.ContentRead, pageId, cancellationToken))
        {
            return CmsResult<CommentSummary>.NotFound(
                $"No page has id {pageId}.",
                WorkflowCodes.NotFound);
        }

        var body = request.Body?.Trim();

        if (string.IsNullOrEmpty(body))
        {
            return CmsResult<CommentSummary>.Invalid(
                WorkflowCodes.CommentInvalid,
                "A comment needs something in it.",
                nameof(CreateCommentRequest.Body));
        }

        if (body.Length > FieldLengths.CommentBody)
        {
            return CmsResult<CommentSummary>.Invalid(
                WorkflowCodes.CommentInvalid,
                $"A comment may be at most {FieldLengths.CommentBody} characters.",
                nameof(CreateCommentRequest.Body));
        }

        var page = await context.Pages
            .AsNoTracking()
            .Include(candidate => candidate.DraftVersion)
            .FirstOrDefaultAsync(candidate => candidate.Id == pageId, cancellationToken);

        if (page is null)
        {
            return CmsResult<CommentSummary>.NotFound(
                $"No page has id {pageId}.",
                WorkflowCodes.NotFound);
        }

        if (request.ParentCommentId is { } parentId)
        {
            // Checked against this page rather than merely for existence: without it a reply is a
            // way to attach text to a thread on a page the caller may not read, addressed by a
            // guessed id.
            var parentPageId = await context.Comments
                .AsNoTracking()
                .Where(comment => comment.Id == parentId)
                .Select(comment => (int?)comment.PageId)
                .FirstOrDefaultAsync(cancellationToken);

            if (parentPageId is null)
            {
                return CmsResult<CommentSummary>.NotFound(
                    $"No comment has id {parentId}.",
                    WorkflowCodes.NotFound);
            }

            if (parentPageId != pageId)
            {
                return CmsResult<CommentSummary>.Invalid(
                    WorkflowCodes.CommentMismatch,
                    "That comment is on a different page.",
                    nameof(CreateCommentRequest.ParentCommentId));
            }
        }

        var comment = new Comment
        {
            PageId = pageId,
            PageVersionId = request.PageVersionId ?? page.DraftVersionId,
            ZoneKey = string.IsNullOrWhiteSpace(request.ZoneKey) ? null : request.ZoneKey.Trim(),
            ParentCommentId = request.ParentCommentId,
            Body = body,
        };

        context.Comments.Add(comment);
        await context.SaveChangesAsync(cancellationToken);

        await NotifyMentionsAsync(comment, page.DraftVersion?.Title ?? $"Page {pageId}", cancellationToken);

        return CmsResult<CommentSummary>.Success(new CommentSummary(
            comment.Id,
            comment.PageId,
            comment.PageVersionId,
            comment.ZoneKey,
            comment.Body,
            comment.CreatedBy,
            await NameAsync(comment.CreatedBy, cancellationToken),
            comment.CreatedOn,
            null,
            null,
            []));
    }

    /// <inheritdoc />
    public async Task<CmsResult<CommentSummary>> ResolveAsync(
        int commentId,
        bool resolved,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<CommentSummary>.Forbidden(
                "Reading pages is not permitted.",
                WorkflowCodes.Forbidden);
        }

        var comment = await context.Comments
            .FirstOrDefaultAsync(candidate => candidate.Id == commentId, cancellationToken);

        if (comment is null
            || !await acl.IsAllowedAsync(CmsPermissions.ContentRead, comment.PageId, cancellationToken))
        {
            return CmsResult<CommentSummary>.NotFound(
                $"No comment has id {commentId}.",
                WorkflowCodes.NotFound);
        }

        if (comment.ParentCommentId is not null)
        {
            return CmsResult<CommentSummary>.Invalid(
                WorkflowCodes.CommentInvalid,
                "Resolve the thread rather than one reply within it.");
        }

        comment.ResolvedOn = resolved ? clock.GetUtcNow() : null;
        comment.ResolvedByUserId = resolved ? users.UserId : null;

        await context.SaveChangesAsync(cancellationToken);

        return CmsResult<CommentSummary>.Success(new CommentSummary(
            comment.Id,
            comment.PageId,
            comment.PageVersionId,
            comment.ZoneKey,
            comment.Body,
            comment.CreatedBy,
            await NameAsync(comment.CreatedBy, cancellationToken),
            comment.CreatedOn,
            comment.ResolvedOn,
            comment.ResolvedByUserId is { } by ? await NameAsync(by, cancellationToken) : null,
            []));
    }

    /// <summary>
    /// Tells anybody named with an <c>@</c> in the body that they were named (spec section 14.8).
    /// </summary>
    /// <remarks>
    /// Matched against user names that actually exist rather than parsed as an address, so
    /// "@ 9am tomorrow" notifies nobody and a typo is silently no mention rather than a failed send.
    /// </remarks>
    private async Task NotifyMentionsAsync(
        Comment comment,
        string pageTitle,
        CancellationToken cancellationToken)
    {
        var handles = MentionPattern().Matches(comment.Body)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (handles.Count == 0) return;

        var normalized = handles.Select(handle => handle.ToUpperInvariant()).ToList();

        var mentioned = await context.Users
            .AsNoTracking()
            .Where(user => user.NormalizedUserName != null && normalized.Contains(user.NormalizedUserName))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        if (mentioned.Count == 0) return;

        await notifications.NotifyManyAsync(
            mentioned,
            NotificationKind.CommentMention,
            comment.PageId,
            pageTitle,
            await NameAsync(users.UserId, cancellationToken),
            comment.Body,
            $"/admin/pages/{comment.PageId}",
            cancellationToken: cancellationToken);
    }

    /// <summary>Nests replies under their parents, in one pass over rows already in id order.</summary>
    private static IReadOnlyList<CommentSummary> Assemble(
        int pageId,
        IReadOnlyList<CommentRow> rows,
        bool includeResolved)
    {
        var repliesByParent = new Dictionary<int, List<CommentSummary>>();

        // Walked newest-first so a reply is assembled before the comment it hangs on, which makes
        // one pass enough however deep the nesting goes.
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            var row = rows[i];
            var children = repliesByParent.TryGetValue(row.Id, out var assembled) ? assembled : [];

            var summary = new CommentSummary(
                row.Id,
                pageId,
                row.PageVersionId,
                row.ZoneKey,
                row.Body,
                row.AuthorUserId,
                row.AuthorName,
                row.CreatedOn,
                row.ResolvedOn,
                row.ResolvedByName,
                children);

            if (row.ParentCommentId is not { } parentId)
            {
                repliesByParent.TryAdd(0, []);
                repliesByParent[0].Insert(0, summary);

                continue;
            }

            if (!repliesByParent.TryGetValue(parentId, out var siblings))
            {
                siblings = [];
                repliesByParent[parentId] = siblings;
            }

            siblings.Insert(0, summary);
        }

        var roots = repliesByParent.TryGetValue(0, out var top) ? top : [];

        return includeResolved ? roots : [.. roots.Where(thread => thread.ResolvedOn is null)];
    }

    private Task<string?> NameAsync(int userId, CancellationToken cancellationToken) =>
        context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.UserName)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>An <c>@handle</c> in a comment body.</summary>
    [GeneratedRegex(@"@([A-Za-z0-9._@+-]{1,256})", RegexOptions.CultureInvariant)]
    private static partial Regex MentionPattern();

    /// <summary>One comment as the query reads it, before the replies are nested.</summary>
    private sealed record CommentRow(
        int Id,
        int? ParentCommentId,
        int? PageVersionId,
        string? ZoneKey,
        string Body,
        int AuthorUserId,
        string? AuthorName,
        DateTimeOffset? CreatedOn,
        DateTimeOffset? ResolvedOn,
        string? ResolvedByName);
}
