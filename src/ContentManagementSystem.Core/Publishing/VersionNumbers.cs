using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Allocates the next version number for a page (task P2-09, spec section 11.3).
/// </summary>
/// <remarks>
/// One definition, because three operations mint a version number — creating a page's first draft,
/// publishing, and taking a checkpoint — and a second copy of "the highest plus one" is a second
/// chance to write "the count plus one", which reuses a number the moment retention removes a row.
/// <para>
/// <strong>The highest ever issued, not the number of rows.</strong> A page's history has gaps in it
/// by design: retention prunes from the middle, and the numbering has to stay strictly increasing
/// across the gap so a version number in an audit entry or a shared preview link never comes to mean
/// a different version later.
/// </para>
/// </remarks>
public static class VersionNumbers
{
    /// <summary>The number a page's first version gets.</summary>
    public const int First = 1;

    /// <summary>
    /// Computes the next number from the numbers already issued.
    /// </summary>
    /// <param name="existing">Every version number the page currently has rows for.</param>
    /// <returns><see cref="First"/> for a page with no versions, and otherwise the highest plus one.</returns>
    public static int Next(IEnumerable<int> existing)
    {
        ArgumentNullException.ThrowIfNull(existing);

        var highest = 0;

        foreach (var number in existing)
        {
            if (number > highest) highest = number;
        }

        return highest + First;
    }

    /// <summary>
    /// Reads the next number for a page from the database.
    /// </summary>
    /// <param name="context">The application database context.</param>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The number to give the next version written for that page.</returns>
    /// <remarks>
    /// The maximum is computed by the database rather than by loading the rows: the history of a
    /// long-lived page is unbounded, and a publish must not pay for it. Two publishes racing for the
    /// same number collide on the <c>(PageId, VersionNumber)</c> unique index rather than silently
    /// producing two version 7s — the publish is inside a transaction that rolls back whole.
    /// </remarks>
    public static async Task<int> NextAsync(
        ApplicationDbContext context,
        int pageId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var highest = await context.PageVersions
            .Where(version => version.PageId == pageId)
            .Select(version => (int?)version.VersionNumber)
            .MaxAsync(cancellationToken);

        return (highest ?? 0) + First;
    }
}
