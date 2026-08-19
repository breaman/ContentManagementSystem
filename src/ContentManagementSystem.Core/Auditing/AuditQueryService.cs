using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Contracts.Workflow;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Auditing;

/// <inheritdoc cref="IAuditQueryService" />
/// <param name="context">The application database context.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
public sealed class AuditQueryService(
    ApplicationDbContext context,
    ICmsAuthorization authorization) : IAuditQueryService
{
    /// <summary>Largest page, whatever was asked for.</summary>
    private const int MaxLimit = 200;

    /// <inheritdoc />
    public async Task<CmsResult<CursorPage<AuditEntrySummary>>> ListAsync(
        AuditQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!authorization.HasPermission(CmsPermissions.AuditView))
        {
            return CmsResult<CursorPage<AuditEntrySummary>>.Forbidden(
                "Reading the audit log is not permitted.",
                PageCodes.Forbidden);
        }

        if (!Cursor.TryDecode(query.Cursor, out var after))
        {
            return CmsResult<CursorPage<AuditEntrySummary>>.Invalid(
                PageCodes.FilterInvalid,
                "That cursor could not be read. Start from the first page rather than assembling " +
                "one by hand.",
                nameof(AuditQuery.Cursor));
        }

        var limit = Math.Clamp(query.Limit, 1, MaxLimit);

        var rows = context.AuditLogs.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            rows = rows.Where(entry => entry.TableName == query.Entity);
        }

        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            // The interceptor stores the key as JSON, such as {"Id":42}, so an exact match on a
            // bare "42" would find nothing. Matched as a substring against that document rather
            // than by parsing it: the shape is the interceptor's business and duplicating its
            // serialization here would be two things to keep in step.
            var needle = $"\"{query.EntityId}\"";
            var bare = $":{query.EntityId}";

            rows = rows.Where(entry =>
                entry.PrimaryKey == query.EntityId
                || entry.PrimaryKey.Contains(needle)
                || entry.PrimaryKey.Contains(bare));
        }

        if (query.UserId is { } userId) rows = rows.Where(entry => entry.UserId == userId);
        if (query.From is { } from) rows = rows.Where(entry => entry.DateTime >= from);
        if (query.To is { } to) rows = rows.Where(entry => entry.DateTime <= to);

        // Descending, and the cursor therefore means "older than this id" — the inverse of every
        // other list in the API, because this is the one list read backwards through time. No
        // cursor decodes to zero, which for an ascending list is a harmless floor and here would be
        // a ceiling that excludes everything; hence the explicit test rather than a bare comparison.
        if (after > 0) rows = rows.Where(entry => entry.Id < after);

        var found = await rows
            .OrderByDescending(entry => entry.Id)
            .Take(limit + 1)
            .Select(entry => new AuditEntrySummary(
                entry.Id,
                entry.TableName,
                entry.PrimaryKey,
                entry.Type,
                entry.UserId,
                context.Users.Where(user => user.Id == entry.UserId)
                    .Select(user => user.UserName).FirstOrDefault(),
                entry.DateTime,
                entry.AffectedColumns,
                entry.OldValues,
                entry.NewValues))
            .ToListAsync(cancellationToken);

        var hasMore = found.Count > limit;

        if (hasMore) found.RemoveAt(found.Count - 1);

        var next = hasMore && found.Count > 0 ? Cursor.Encode(found[^1].Id) : null;

        return CmsResult<CursorPage<AuditEntrySummary>>.Success(
            new CursorPage<AuditEntrySummary>(found, next));
    }
}
