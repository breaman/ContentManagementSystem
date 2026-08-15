using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Routing;

/// <inheritdoc cref="INotFoundLogService" />
/// <param name="context">The application database context.</param>
/// <param name="clock">Source of the current time, stamped on first and last sighting.</param>
/// <param name="logger">Log for a write that failed, which is never allowed to reach the visitor.</param>
public sealed class NotFoundLogService(
    ApplicationDbContext context,
    TimeProvider clock,
    ILogger<NotFoundLogService> logger) : INotFoundLogService
{
    /// <inheritdoc />
    public async Task RecordAsync(
        string? url,
        string? referrer,
        CancellationToken cancellationToken = default)
    {
        var normalized = SiteUrls.Normalize(url);
        var hash = SiteUrls.Hash(normalized);
        var now = clock.GetUtcNow();
        var trimmedReferrer = Trim(referrer);

        try
        {
            // Update first, insert second. The overwhelming majority of 404s are repeats of a URL
            // already in the table — that is the entire premise of the report — so the common path
            // is one relative UPDATE that never enters the change tracker and cannot lose a
            // concurrent increment.
            var rows = context.NotFoundLogs.Where(entry => entry.UrlHash == hash);

            // Two shapes of the same update, because a request that carried no referrer must not
            // erase the one a previous request supplied: "who is still linking to this" is the
            // question the column exists to answer, and one live example is enough to go and ask.
            var updated = trimmedReferrer is null
                ? await rows.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(entry => entry.HitCount, entry => entry.HitCount + 1)
                        .SetProperty(entry => entry.LastSeenOn, now),
                    cancellationToken)
                : await rows.ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(entry => entry.HitCount, entry => entry.HitCount + 1)
                        .SetProperty(entry => entry.LastSeenOn, now)
                        .SetProperty(entry => entry.Referrer, trimmedReferrer),
                    cancellationToken);

            if (updated > 0) return;

            context.NotFoundLogs.Add(new NotFoundLog
            {
                Url = normalized,
                UrlHash = hash,
                Referrer = trimmedReferrer,
                HitCount = 1,
                FirstSeenOn = now,
                LastSeenOn = now,
            });

            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests for the same brand-new dead URL raced to insert it and the unique index
            // caught the loser. That is an ordinary outcome rather than a fault, and the right
            // repair is the update the winner has now made possible.
            context.ChangeTracker.Clear();

            await RetryIncrementAsync(hash, now, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Failed to record a 404 for '{Url}'.", normalized);
        }
    }

    private async Task RetryIncrementAsync(byte[] hash, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            await context.NotFoundLogs
                .Where(entry => entry.UrlHash == hash)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(entry => entry.HitCount, entry => entry.HitCount + 1)
                        .SetProperty(entry => entry.LastSeenOn, now),
                    cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // One retry, then give up. A hit count that is low by one is a worse report; a 404 page
            // that fails to render is a worse site.
            logger.LogWarning(exception, "Failed to count a repeat 404 after an insert race.");
        }
    }

    /// <summary>
    /// Cuts a referrer down to what the column holds.
    /// </summary>
    /// <remarks>
    /// The referrer is attacker-controlled: it arrives in a request header, and nothing about a
    /// 404 requires the sender to be a browser. Truncating rather than refusing keeps a
    /// deliberately over-long header from turning every 404 into a failed write, and the value is
    /// only ever shown to an administrator in the report.
    /// </remarks>
    private static string? Trim(string? referrer)
    {
        if (string.IsNullOrWhiteSpace(referrer)) return null;

        var trimmed = referrer.Trim();

        return trimmed.Length <= FieldLengths.Url ? trimmed : trimmed[..FieldLengths.Url];
    }
}
