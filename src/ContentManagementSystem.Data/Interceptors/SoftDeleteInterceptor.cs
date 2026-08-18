using ContentManagementSystem.Data.Interfaces;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentManagementSystem.Data.Interceptors;

/// <summary>
/// Rewrites hard deletes of soft-deletable entities into flag updates.
/// </summary>
/// <remarks>
/// This is a safety net, not the intended path: services expose explicit deactivate/restore
/// operations. It exists so that a stray <c>Remove</c> call cannot destroy a row that order or
/// invoice history still references — or, in the CMS, take a page's entire version history with
/// it, which is exactly what the recycle bin exists to prevent (spec section 23.5).
/// <para>
/// It is registered first, so the rewritten entry is stamped by
/// <see cref="FingerPrintInterceptor"/> and recorded by <see cref="AuditLogInterceptor"/> as the
/// update it has become rather than as the delete it was written as. An entity already flagged
/// deleted is left <see cref="EntityState.Deleted"/>: reaching <c>Remove</c> a second time is the
/// permanent delete the recycle bin performs deliberately, and turning that into a no-op would make
/// purging impossible.
/// </para>
/// <para>
/// Cascades are out of reach here, as they were when this ran from a <c>SaveChanges</c> override:
/// <c>AuthDbContext</c> defers them to save time, and EF computes them after every interceptor has
/// run. What the deferral buys is that severing a required relationship no longer throws inside
/// <c>Remove</c>, which is what used to bypass this net entirely whenever the dependents happened
/// to be loaded.
/// </para>
/// </remarks>
/// <param name="users">Who the caller is. Null outside a request, as in design-time tooling.</param>
/// <param name="clock">The clock <c>DeletedOn</c> is read from.</param>
public sealed class SoftDeleteInterceptor(IUserService? users, TimeProvider clock) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            RewriteDeletes(context);
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            RewriteDeletes(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Turns every pending delete of a not-yet-deleted <see cref="ISoftDeletable"/> into an update.
    /// </summary>
    /// <param name="context">The context about to save.</param>
    /// <remarks>
    /// Public rather than private because it is the seam the unit tests drive: the rewrite is
    /// change-tracker work with no SQL in it, so a test can assert it against a context that has
    /// never opened a connection.
    /// </remarks>
    /// <example>
    /// <code>
    /// context.Pages.Remove(page);
    /// new SoftDeleteInterceptor(users, TimeProvider.System).RewriteDeletes(context);
    /// // context.Entry(page).State is now EntityState.Modified, and page.IsDeleted is true.
    /// </code>
    /// </example>
    public void RewriteDeletes(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State is not EntityState.Deleted || entry.Entity.IsDeleted) continue;

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedOn = clock.GetUtcNow();
            entry.Entity.DeletedBy = users?.UserId;
        }
    }
}
