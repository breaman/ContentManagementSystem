using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Grants exactly the permissions it was given, and nothing else.
/// </summary>
/// <param name="permissions">What the caller holds.</param>
public sealed class StubAuthorization(params string[] permissions) : ICmsAuthorization
{
    /// <summary>Everything a Phase 2 service checks, for the tests that are not about permissions.</summary>
    public static StubAuthorization Everything { get; } = new(
        CmsPermissions.ContentRead,
        CmsPermissions.ContentEdit,
        CmsPermissions.ContentPublish,
        CmsPermissions.ContentDelete,
        CmsPermissions.UsersManage);

    /// <inheritdoc />
    public bool HasPermission(string permission) => permissions.Contains(permission);
}

/// <summary>A fixed caller identity, since these suites never go through a request.</summary>
public sealed class StubUserService : IUserService
{
    /// <summary>Identity handed out unless a test changes it mid-run.</summary>
    public int UserId { get; set; } = 1;
}

/// <summary>
/// Boots the real service graph against a throwaway database, with the caller's identity and
/// permissions under the test's control (tasks P2-08 to P2-15).
/// </summary>
/// <remarks>
/// The services are <em>resolved</em>, not constructed by hand, so the container wiring is under
/// test too — a scoped service accidentally registered as a singleton, or a dependency nobody
/// registered, fails here rather than at startup on somebody's machine. Only the two things that
/// genuinely need a request are replaced: outside one, the real authorization answers "no
/// permissions" and the real user service answers "user 0", which are correct behaviours and not
/// what these suites are about.
/// </remarks>
public sealed class PageWorkbench : IAsyncDisposable
{
    private readonly CmsApplicationFactory _factory;
    private readonly AsyncServiceScope _scope;

    private PageWorkbench(CmsApplicationFactory factory, StubAuthorization authorization, FakeTimeProvider clock)
    {
        _factory = factory;
        _scope = factory.Services.CreateAsyncScope();
        Authorization = authorization;
        Clock = clock;
    }

    /// <summary>The caller's permissions, as this workbench was built with them.</summary>
    public StubAuthorization Authorization { get; }

    /// <summary>The clock the services read, so expiry and retention are testable without waiting.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>Who the caller is.</summary>
    public StubUserService Users => (StubUserService)Resolve<IUserService>();

    /// <summary>The database, for arranging fixtures and asserting against stored rows.</summary>
    public ApplicationDbContext Context => Resolve<ApplicationDbContext>();

    /// <summary>Resolves a service from the request scope.</summary>
    /// <typeparam name="T">Service type.</typeparam>
    public T Resolve<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// Creates a workbench over a freshly migrated database.
    /// </summary>
    /// <param name="fixture">The container fixture supplying the SQL Server instance.</param>
    /// <param name="authorization">What the caller may do. Defaults to everything.</param>
    /// <param name="cancellationToken">Token observed while migrating.</param>
    /// <param name="interceptor">
    /// An EF interceptor to add, for the suite that injects a failure mid-transaction.
    /// </param>
    public static async Task<PageWorkbench> CreateAsync(
        SqlServerFixture fixture,
        StubAuthorization? authorization = null,
        CancellationToken cancellationToken = default,
        IInterceptor? interceptor = null)
    {
        var granted = authorization ?? StubAuthorization.Everything;
        var clock = new FakeTimeProvider();

        var factory = await CmsApplicationFactory.CreateAsync(fixture, cancellationToken, (services, connectionString) =>
        {
            services.RemoveAll<ICmsAuthorization>();
            services.AddSingleton<ICmsAuthorization>(granted);
            services.RemoveAll<IUserService>();
            services.AddScoped<IUserService, StubUserService>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);

            if (interceptor is not null)
            {
                // Re-registered rather than added to DI: EF resolves interceptors from the options
                // it was built with, and the application's registration was already made without
                // this one.
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<DbContextOptions>();
                services.AddDbContext<ApplicationDbContext>(options => options
                    .UseSqlServer(connectionString)
                    .AddInterceptors(interceptor));
            }
        });

        return new PageWorkbench(factory, granted, clock);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _factory.DisposeAsync();
    }

    /// <summary>
    /// Inserts a template with the given zones and cuts its first revision, as the structure API does.
    /// </summary>
    /// <param name="key">Key of the template.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="zones">The zone definitions.</param>
    public async Task<Template> AddTemplateAsync(
        string key,
        CancellationToken cancellationToken,
        params Zone[] zones)
    {
        var template = new Template { Key = key, Name = key, CurrentRevision = 1, IsEnabled = true };

        foreach (var zone in zones)
        {
            template.Zones.Add(zone);
        }

        template.Revisions.Add(new TemplateRevision
        {
            RevisionNumber = 1,
            ZoneSnapshotJson = ContentSchemaSnapshot.WriteZones(zones),
            Notes = "Template created.",
        });

        Context.Templates.Add(template);
        await Context.SaveChangesAsync(cancellationToken);

        return template;
    }

    /// <summary>Creates a page through the real service, failing loudly if it was refused.</summary>
    public async Task<PageDetail> AddPageAsync(
        Template template,
        string title,
        CancellationToken cancellationToken,
        int? parentId = null)
    {
        var result = await Resolve<IPageService>().CreateAsync(
            new CreatePageRequest(template.Id, title, parentId),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(
            string.Join("; ", result.Diagnostics.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));

        return result.Value!;
    }

    /// <summary>Reads a page's stored draft row directly.</summary>
    public Task<PageVersion> DraftOfAsync(int pageId, CancellationToken cancellationToken) =>
        Context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == pageId && version.Status == PageVersionStatus.Draft)
            .SingleAsync(cancellationToken);

    /// <summary>A zone definition holding plain text.</summary>
    public static Zone TextZone(string key, bool required = false) =>
        new() { Key = key, Name = key, FieldTypeKey = FieldTypeKeys.PlainText, IsRequired = required };

    /// <summary>A zone definition holding a reference to another page.</summary>
    public static Zone PageReferenceZone(string key) =>
        new() { Key = key, Name = key, FieldTypeKey = FieldTypeKeys.PageReference };

    /// <summary>A zone definition holding blocks.</summary>
    public static Zone BlocksZone(string key) =>
        new() { Key = key, Name = key, FieldTypeKey = FieldTypeKeys.Blocks };
}

/// <summary>
/// A clock the test moves by hand.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from <c>Microsoft.Extensions.TimeProvider.Testing</c>: the two
/// things these suites need from it are "what time is it" and "move forward", and adding a package
/// to central package management for that is more moving parts than the code it replaces.
/// </remarks>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="delta">How far.</param>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
