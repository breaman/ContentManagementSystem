using ContentManagementSystem.Data.Interceptors;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.TestSupport;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using NSubstitute;

namespace ContentManagementSystem.Data.Tests.Interceptors;

/// <summary>
/// What each save interceptor does to the change tracker, asserted without a database.
/// </summary>
/// <remarks>
/// Every one of these ran through a real SQL Server container while the behaviour lived in a
/// <c>SaveChanges</c> override, because reaching it meant saving. None of it is SQL: it is entity
/// states, property values, and rows added to the tracker, all of which a context that has never
/// opened a connection holds perfectly well. The container suites still cover the wiring — that the
/// application's context is built with these interceptors at all — which is the part these cannot
/// see.
/// </remarks>
public class SaveInterceptorTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Test]
    public void RemovingASoftDeletableEntityBecomesAFlagUpdate()
    {
        using var context = Context();
        var page = TrackedPage(context);
        var users = User(42);

        context.Remove(page);
        new SoftDeleteInterceptor(users, Clock()).RewriteDeletes(context);

        context.Entry(page).State.Should().Be(EntityState.Modified);
        page.IsDeleted.Should().BeTrue();
        page.DeletedOn.Should().Be(Now);
        page.DeletedBy.Should().Be(42);
    }

    [Test]
    public void RemovingAnAlreadyDeletedEntityStaysADelete()
    {
        using var context = Context();
        var page = TrackedPage(context);
        page.IsDeleted = true;

        context.Remove(page);
        new SoftDeleteInterceptor(User(42), Clock()).RewriteDeletes(context);

        // Reaching Remove a second time is the permanent delete the recycle bin performs
        // deliberately. Rewriting it too would leave nothing able to purge a row.
        context.Entry(page).State.Should().Be(EntityState.Deleted);
    }

    [Test]
    public void AnInsertIsStampedWithBothCreationAndModification()
    {
        using var context = Context();
        var page = NewPage();

        context.Add(page);
        new FingerPrintInterceptor(User(7), Clock()).Stamp(context);

        page.CreatedBy.Should().Be(7);
        page.CreatedOn.Should().Be(Now);
        page.ModifiedBy.Should().Be(7);
        page.ModifiedOn.Should().Be(Now);
    }

    [Test]
    public void AnUpdateIsStampedWithoutDisturbingTheCreationAttribution()
    {
        using var context = Context();
        var page = TrackedPage(context);
        page.CreatedBy = 3;
        page.CreatedOn = Now.AddDays(-1);
        context.Entry(page).State = EntityState.Modified;

        new FingerPrintInterceptor(User(7), Clock()).Stamp(context);

        page.CreatedBy.Should().Be(3);
        page.CreatedOn.Should().Be(Now.AddDays(-1));
        page.ModifiedBy.Should().Be(7);
        page.ModifiedOn.Should().Be(Now);
    }

    [Test]
    public void AnUntouchedEntityIsNotStamped()
    {
        using var context = Context();
        var page = TrackedPage(context);

        new FingerPrintInterceptor(User(7), Clock()).Stamp(context);

        page.ModifiedOn.Should().BeNull();
        context.Entry(page).State.Should().Be(EntityState.Unchanged);
    }

    [Test]
    public void AnInsertIsCapturedAsACreateCarryingItsNewValues()
    {
        using var context = Context();
        var page = NewPage();

        context.Add(page);
        new AuditLogInterceptor(User(7), Clock()).Capture(context);

        var audit = Audits(context).Should().ContainSingle().Subject;
        audit.TableName.Should().Be(nameof(Page));
        audit.Type.Should().Be(nameof(AuditType.Create));
        audit.UserId.Should().Be(7);
        audit.DateTime.Should().Be(Now);
        audit.NewValues.Should().Contain(@"""Slug"":""home""");
        audit.OldValues.Should().BeNull();
    }

    [Test]
    public void AnUpdateIsCapturedWithOnlyTheColumnsThatChanged()
    {
        using var context = Context();
        var page = TrackedPage(context);

        page.Slug = "about";
        new AuditLogInterceptor(User(7), Clock()).Capture(context);

        var audit = Audits(context).Should().ContainSingle().Subject;
        audit.Type.Should().Be(nameof(AuditType.Update));
        audit.AffectedColumns.Should().Be(@"[""Slug""]");
        audit.OldValues.Should().Contain(@"""Slug"":""home""");
        audit.NewValues.Should().Contain(@"""Slug"":""about""");
    }

    [Test]
    public void AnExemptEntityIsNotCaptured()
    {
        using var context = Context();

        // Written every 30 seconds per open editor. The exclusion list is the whole reason this
        // interceptor knows entity names at all, so it is worth an assertion of its own.
        context.Add(new EditLock
        {
            PageId = 1,
            UserId = 2,
            AcquiredOn = Now,
            HeartbeatOn = Now,
        });
        new AuditLogInterceptor(User(7), Clock()).Capture(context);

        Audits(context).Should().BeEmpty();
    }

    [Test]
    public void AuditRowsAreNotThemselvesAudited()
    {
        using var context = Context();

        context.Add(new AuditLog { Type = nameof(AuditType.Create), TableName = nameof(Page), PrimaryKey = "{}" });
        new AuditLogInterceptor(User(7), Clock()).Capture(context);

        // Auditing the audit table would double every row it writes, and then double that.
        Audits(context).Should().ContainSingle();
    }

    [Test]
    public void TheInterceptorsRunInTheOrderThatMakesASoftDeleteReadAsAnUpdate()
    {
        using var context = Context();
        var page = TrackedPage(context);

        context.Remove(page);
        foreach (var interceptor in CmsSaveInterceptors.Create(User(7), Clock()))
        {
            switch (interceptor)
            {
                case SoftDeleteInterceptor soft: soft.RewriteDeletes(context); break;
                case FingerPrintInterceptor stamp: stamp.Stamp(context); break;
                case AuditLogInterceptor capture: capture.Capture(context); break;
            }
        }

        // The order is the behaviour: had audit capture run first, the recycle bin's delete would be
        // recorded as a delete of a row that is still there, with no record of who retired it.
        var audit = Audits(context).Should().ContainSingle().Subject;
        audit.Type.Should().Be(nameof(AuditType.Update));
        audit.AffectedColumns.Should().Contain(nameof(ISoftDeletable.IsDeleted));
        audit.AffectedColumns.Should().Contain(nameof(FingerPrintEntityBase.ModifiedOn));
        page.ModifiedOn.Should().Be(Now);
    }

    /// <summary>A clock frozen at <see cref="Now"/>.</summary>
    /// <remarks>
    /// Frozen at the instant the class was loaded rather than at a literal date: a hard-coded start
    /// drifts further from the wall clock every day the repository exists, which is how
    /// <c>RetentionKeepsWhatAnEditorWouldBeUpsetToLose</c> came to pass until one minute in August
    /// 2026 and fail from then on.
    /// </remarks>
    private static TimeProvider Clock() => new FrozenClock(Now);

    private static IUserService User(int id)
    {
        var users = Substitute.For<IUserService>();
        users.UserId.Returns(id);

        return users;
    }

    /// <summary>A context with a built model and no connection behind it.</summary>
    /// <summary>A context that never opens a connection, holding the same model as every other.</summary>
    /// <remarks>
    /// The connection string is deliberately unreachable — nothing here saves. The application
    /// service provider is not optional even so: a context built without it has a different Identity
    /// schema, and one of those in the process is enough to fail every suite that migrates. See
    /// <see cref="IdentityModelServices"/>.
    /// </remarks>
    private static ApplicationDbContext Context() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=False")
            .UseApplicationServiceProvider(IdentityModelServices.Instance)
            .Options);

    /// <summary>A page the context believes it read from the database.</summary>
    private static Page TrackedPage(ApplicationDbContext context)
    {
        var page = NewPage();
        page.Id = 1;
        context.Attach(page);

        return page;
    }

    private static Page NewPage() => new()
    {
        Slug = "home",
        Path = "/1/",
        PublicId = Guid.NewGuid(),
        TemplateId = 1,
    };

    private static IEnumerable<AuditLog> Audits(ApplicationDbContext context) =>
        context.ChangeTracker.Entries<AuditLog>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity);

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
