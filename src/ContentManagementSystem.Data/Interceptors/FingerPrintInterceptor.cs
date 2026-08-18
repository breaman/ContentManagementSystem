using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentManagementSystem.Data.Interceptors;

/// <summary>
/// Stamps created and modified attribution onto every <see cref="FingerPrintEntityBase"/> being saved.
/// </summary>
/// <remarks>
/// <b>The timestamps come from an injected <see cref="TimeProvider"/> rather than from
/// <see cref="DateTimeOffset.UtcNow"/>, because a stamp that ignores the container's clock cannot be
/// tested against anything that honours it.</b> The retention sweep computes its cutoff from the
/// registered <see cref="TimeProvider"/> and compares it to <c>CreatedOn</c> written here; when the
/// two came from different clocks, a suite that advanced the fake one moved the cutoff and left the
/// rows behind, so whether a test passed depended on the real calendar date it ran on. Anything that
/// later ages a row out — scheduled publishing, recycle-bin purging, audit retention — needs the
/// same seam.
/// <para>
/// One instant is read per save and shared by every entity in it, so a page and the version it was
/// saved with cannot differ by a tick.
/// </para>
/// <para>
/// The stamps are written to the entities themselves rather than through the entry API, and EF picks
/// them up because <c>SavingChanges</c> interceptors run <em>before</em> <c>SaveChanges</c> detects
/// changes. Registration order therefore matters — see <see cref="CmsSaveInterceptors"/>.
/// </para>
/// </remarks>
/// <param name="users">Who the caller is. Null outside a request, as in design-time tooling.</param>
/// <param name="clock">The clock every stamped timestamp is read from.</param>
public sealed class FingerPrintInterceptor(IUserService? users, TimeProvider clock) : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Stamp(context);
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
            Stamp(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Writes creation and modification attribution onto every fingerprinted entity being saved.
    /// </summary>
    /// <param name="context">The context about to save.</param>
    /// <remarks>
    /// Public rather than private for the same reason as
    /// <see cref="SoftDeleteInterceptor.RewriteDeletes"/>: it is change-tracker work with no SQL in
    /// it, so a test can drive it against a context that has never opened a connection.
    /// </remarks>
    /// <example>
    /// <code>
    /// context.Pages.Add(page);
    /// new FingerPrintInterceptor(users, clock).Stamp(context);
    /// // page.CreatedOn and page.ModifiedOn now carry the clock's instant.
    /// </code>
    /// </example>
    public void Stamp(DbContext context)
    {
        var now = clock.GetUtcNow();
        var userId = users?.UserId ?? default;

        foreach (var entry in context.ChangeTracker.Entries<FingerPrintEntityBase>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedBy = userId;
                    entry.Entity.CreatedOn = now;
                    entry.Entity.ModifiedBy = userId;
                    entry.Entity.ModifiedOn = now;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedBy = userId;
                    entry.Entity.ModifiedOn = now;
                    break;
            }
        }
    }
}
