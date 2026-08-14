using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interfaces;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContentManagementSystem.Data.Models;

public abstract class AuthDbContext : IdentityDbContext<User, Role, int>
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
    private static readonly HashSet<string> AuditExemptEntityNames = new(StringComparer.Ordinal)
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

    private readonly IUserService? _userService;

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected AuthDbContext(DbContextOptions options) : base(options)
    {
        DeferCascadesToSaveChanges();
    }

    protected AuthDbContext(DbContextOptions options, IUserService userService) :
        base(options)
    {
        _userService = userService;
        DeferCascadesToSaveChanges();
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplySoftDeletes();
        AddFingerPrinting();
        AddApplicationInfo();
        AddLogging();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplySoftDeletes();
        AddFingerPrinting();
        AddApplicationInfo();
        AddLogging();
        return base.SaveChanges();
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Model-wide defaults so no column silently falls back to a lossy provider default.
        // Types that need different precision (tax rates) override these in their own
        // IEntityTypeConfiguration.
        configurationBuilder.Properties<decimal>().HaveColumnType(ColumnTypes.Money);
        configurationBuilder.Properties<DateTimeOffset>().HaveColumnType(ColumnTypes.Timestamp);
    }

    protected virtual void AddApplicationInfo()
    {
    }

    protected virtual void ModifyAddedEntity(EntityEntry entry)
    {
    }

    protected virtual void ModifyExistingEntity(EntityEntry entry)
    {
    }

    /// <summary>
    /// Rewrites hard deletes of soft-deletable entities into flag updates.
    /// </summary>
    /// <remarks>
    /// This is a safety net, not the intended path: services expose explicit deactivate/restore
    /// operations. It exists so that a stray <c>Remove</c> call cannot destroy a row that order or
    /// invoice history still references — or, in the CMS, take a page's entire version history with
    /// it, which is exactly what the recycle bin exists to prevent (spec section 23.5).
    /// <para>
    /// It runs before fingerprinting and audit capture, so the rewritten entry is stamped and logged
    /// as the update it has become rather than as the delete it was written as. An entity already
    /// flagged deleted is left <see cref="EntityState.Deleted"/>: reaching <c>Remove</c> a second
    /// time is the permanent delete the recycle bin performs deliberately, and turning that into a
    /// no-op would make purging impossible.
    /// </para>
    /// </remarks>
    protected virtual void ApplySoftDeletes()
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State is not EntityState.Deleted || entry.Entity.IsDeleted) continue;

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedOn = DateTimeOffset.UtcNow;
            entry.Entity.DeletedBy = _userService?.UserId;
        }
    }

    /// <summary>
    /// Moves cascade and orphan handling from <c>Remove</c> to <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="ApplySoftDeletes"/> is a net with a hole in it. EF's default timing
    /// resolves severed required relationships the instant <c>Remove</c> is called, so removing a
    /// page whose versions happen to be loaded throws there — before any override of
    /// <c>SaveChanges</c> gets a chance to rewrite the delete into a flag update. The same call
    /// against a page whose versions are <em>not</em> loaded succeeds and is caught, which makes the
    /// safety net's behaviour depend on what the change tracker happened to be holding.
    /// <para>
    /// Deferring changes nothing about the SQL that is finally sent; it only decides when the change
    /// tracker computes it.
    /// </para>
    /// </remarks>
    private void DeferCascadesToSaveChanges()
    {
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }

    private void AddFingerPrinting()
    {
        var modified = ChangeTracker.Entries().Where(e => e.State == EntityState.Modified);
        var added = ChangeTracker.Entries().Where(e => e.State == EntityState.Added);
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in added)
        {
            if (entry.Entity is FingerPrintEntityBase fingerPrintEntry)
            {
                fingerPrintEntry.CreatedBy = _userService?.UserId ?? default;
                fingerPrintEntry.CreatedOn = now;
                fingerPrintEntry.ModifiedBy = _userService?.UserId ?? default;
                fingerPrintEntry.ModifiedOn = now;
            }
            ModifyAddedEntity(entry);
        }

        foreach (var entry in modified)
        {
            if (entry.Entity is FingerPrintEntityBase fingerPrintEntry)
            {
                fingerPrintEntry.ModifiedBy = _userService?.UserId ?? default;
                fingerPrintEntry.ModifiedOn = now;
            }
            ModifyExistingEntity(entry);
        }
    }

    private void AddLogging()
    {
        ChangeTracker.DetectChanges();
        var auditEntries = new List<AuditEntry>();
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog || entry.State == EntityState.Detached ||
                entry.State == EntityState.Unchanged) continue;

            var entityName = entry.Entity.GetType().Name;
            if (AuditExemptEntityNames.Contains(entityName)) continue;

            var auditEntry = new AuditEntry(entry)
            {
                TableName = entityName,
                UserId = _userService?.UserId ?? default
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

        foreach (var auditEntry in auditEntries) AuditLogs.Add(auditEntry.ToAuditLog());
    }
}