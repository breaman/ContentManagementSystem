using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentManagementSystem.Data.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every insert, update, and delete in a save.
/// </summary>
/// <remarks>
/// Registered last, so it records what the earlier interceptors left behind: a soft delete appears
/// as the update it was rewritten into, and the fingerprints stamped onto an entity are part of the
/// values captured for it.
/// <para>
/// The rows are added to the same context and therefore go to the database inside the same
/// transaction as the change they describe. An audit row that could commit without its change, or
/// the change without its row, would be worse than no audit trail at all.
/// </para>
/// </remarks>
/// <param name="users">Who the caller is. Null outside a request, as in design-time tooling.</param>
/// <param name="clock">The clock each row's timestamp is read from.</param>
public sealed class AuditLogInterceptor(IUserService? users, TimeProvider clock) : SaveChangesInterceptor
{
    /// <summary>
    /// Entity types deliberately excluded from audit capture.
    /// </summary>
    /// <remarks>
    /// Every one of these is high-churn derived data written by a background service rather than by
    /// a person: search projections, outbox rows, generated image renditions, editor heartbeats,
    /// and 404 hit counters. Auditing them grows <c>AuditLog</c> without bound and slows every
    /// <c>SaveChanges</c>, while recording nothing anybody would ever ask about — the source of
    /// truth they derive from is audited already (spec section 23.5).
    /// <para>
    /// Names are matched rather than types because the tables arrive across several phases; the
    /// exclusion is registered up front so a later phase cannot land the table and forget the
    /// exclusion. Keep this list in step with the guidance in <c>CONTRIBUTING.md</c>.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> ExemptEntityNames = new(StringComparer.Ordinal)
    {
        "SearchDocument",
        "OutboxMessage",
        "MediaRendition",
        "EditLock",
        "NotFoundLog",

        // Beyond the list in spec section 23.5, and deliberately so. ContentReference rows are a
        // projection of the payload, deleted and reinserted wholesale on every draft save — which
        // happens every twenty seconds per open editor. Auditing them would multiply the audit
        // table by the number of references on a page, per autosave, to record something already
        // recoverable from the payload that is audited beside it.
        "ContentReference",
    };

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            Capture(context);
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
            Capture(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// Adds one audit row per tracked change to the context that is about to save.
    /// </summary>
    /// <param name="context">The context about to save.</param>
    /// <remarks>
    /// Public rather than private for the same reason as
    /// <see cref="SoftDeleteInterceptor.RewriteDeletes"/>: it is change-tracker work with no SQL in
    /// it, so a test can drive it against a context that has never opened a connection.
    /// <para>
    /// The rows are built before any of them is added, because adding to the context while
    /// enumerating its entries would mutate the collection being read.
    /// </para>
    /// </remarks>
    public void Capture(DbContext context)
    {
        context.ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached ||
                entry.State == EntityState.Unchanged) continue;

            var entityName = entry.Entity.GetType().Name;
            if (ExemptEntityNames.Contains(entityName)) continue;

            var auditEntry = new AuditEntry(entry)
            {
                TableName = entityName,
                UserId = users?.UserId ?? default
            };
            auditEntries.Add(auditEntry);
            foreach (var property in entry.Properties)
            {
                var propertyName = property.Metadata.Name;
                if (property.Metadata.IsPrimaryKey()) auditEntry.KeyValues[propertyName] = property.CurrentValue!;
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditEntry.AuditType = AuditType.Create;
                        auditEntry.NewValues[propertyName] = property.CurrentValue!;
                        break;
                    case EntityState.Deleted:
                        auditEntry.AuditType = AuditType.Delete;
                        auditEntry.OldValues[propertyName] = property.OriginalValue!;
                        break;
                    case EntityState.Modified:
                        if (property.IsModified)
                        {
                            auditEntry.ChangedColumns.Add(propertyName);
                            auditEntry.AuditType = AuditType.Update;
                            auditEntry.OldValues[propertyName] = property.OriginalValue!;
                            auditEntry.NewValues[propertyName] = property.CurrentValue!;
                        }

                        break;
                    case EntityState.Detached:
                        break;
                    case EntityState.Unchanged:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        foreach (var auditEntry in auditEntries) context.Set<AuditLog>().Add(auditEntry.ToAuditLog(clock));
    }
}
