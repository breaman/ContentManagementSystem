using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interceptors;

using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContentManagementSystem.Data.Models;

/// <summary>
/// The Identity model plus the audit table, and nothing about what a save does.
/// </summary>
/// <remarks>
/// Soft-delete rewriting, fingerprint stamping, and audit capture used to run from overrides of
/// <c>SaveChanges</c> here. They are <see cref="SoftDeleteInterceptor"/>,
/// <see cref="FingerPrintInterceptor"/>, and <see cref="AuditLogInterceptor"/> now, registered on
/// the options the context is built from — see <see cref="CmsSaveInterceptors"/> for the order they
/// run in and for what a context built without them silently loses.
/// <para>
/// What that bought is that the context no longer takes services. It needed <c>IUserService</c> and
/// <c>TimeProvider</c> only to feed those three concerns, and taking them cost three constructor
/// overloads, an <c>[ActivatorUtilitiesConstructor]</c> to tell the factory's activator which one to
/// use, and a test fixture that built the parameterless one and so quietly stamped from the wall
/// clock with no user attached.
/// </para>
/// </remarks>
public abstract class AuthDbContext : IdentityDbContext<User, Role, int>
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected AuthDbContext(DbContextOptions options) : base(options)
    {
        DeferCascadesToSaveChanges();
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

    /// <summary>
    /// Moves cascade and orphan handling from <c>Remove</c> to <c>SaveChanges</c>.
    /// </summary>
    /// <remarks>
    /// Without this, <see cref="SoftDeleteInterceptor"/> is a net with a hole in it. EF's default
    /// timing resolves severed required relationships the instant <c>Remove</c> is called, so
    /// removing a page whose versions happen to be loaded throws there — before anything gets a
    /// chance to rewrite the delete into a flag update. The same call against a page whose versions
    /// are <em>not</em> loaded succeeds and is caught, which makes the safety net's behaviour depend
    /// on what the change tracker happened to be holding.
    /// <para>
    /// Deferring changes nothing about the SQL that is finally sent; it only decides when the change
    /// tracker computes it. It stays on the context rather than moving to an interceptor because it
    /// has to be in force from the moment the context exists — by the time a save begins, the
    /// <c>Remove</c> it protects has already happened.
    /// </para>
    /// </remarks>
    private void DeferCascadesToSaveChanges()
    {
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }
}
