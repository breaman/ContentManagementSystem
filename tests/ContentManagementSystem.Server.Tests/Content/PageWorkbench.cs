using System.Text.Json;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
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
    /// An HTTP client against the same application and the same database this workbench arranges.
    /// </summary>
    /// <param name="followRedirects">
    /// Whether the client follows a 3xx. False for anything asserting on a redirect itself, since a
    /// client that followed it reports the destination's status and hides the one under test.
    /// </param>
    /// <remarks>
    /// What makes the delivery suite possible: content is arranged through the real services and
    /// then requested over HTTP, rather than arranged in one process and read in another. Note that
    /// the workbench's permissive <c>ICmsAuthorization</c> is in force for these requests too — it is
    /// irrelevant to public delivery, which authorizes nothing, but it would be wrong to write an
    /// authorization test through this client.
    /// </remarks>
    public HttpClient CreateClient(bool followRedirects = true, params string[] roles)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = followRedirects,
        });

        // No roles means no identity at all rather than an identity holding nothing — the two
        // produce different status codes, and the preview suite asserts on that difference.
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, string.Join(',', roles));
        }

        return client;
    }

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
    /// <param name="key">
    /// Key of the template. It must be one <em>no deployed component declares</em>: the startup
    /// reconciler has already inserted a row for every <c>[CmsTemplate]</c> in the solution, so
    /// <c>article</c> and <c>marketing-landing</c> collide on the unique key here. Use
    /// <see cref="UseTemplateAsync"/> when the test needs a template that actually renders.
    /// </param>
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

    /// <summary>
    /// Gives an already-reconciled template its zone definitions.
    /// </summary>
    /// <param name="key">Key of a template a deployed component declares.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="zones">The zone definitions.</param>
    /// <remarks>
    /// <see cref="AddTemplateAsync"/> cannot be used for a template whose key is declared in code:
    /// the startup reconciler has already inserted the row, with revision 1 and no zones, and a
    /// second insert collides on the unique key. This finds that row and writes the zones into the
    /// revision it already cut — legitimate only because no content exists yet, which is true of
    /// every fixture that calls it.
    /// </remarks>
    public async Task<Template> UseTemplateAsync(
        string key,
        CancellationToken cancellationToken,
        params Zone[] zones)
    {
        var template = await Context.Templates
            .Include(candidate => candidate.Zones)
            .Include(candidate => candidate.Revisions)
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (template is null) return await AddTemplateAsync(key, cancellationToken, zones);

        foreach (var zone in zones)
        {
            template.Zones.Add(zone);
        }

        var revision = template.Revisions.Single(candidate => candidate.RevisionNumber == 1);
        revision.ZoneSnapshotJson = ContentSchemaSnapshot.WriteZones(zones);
        template.CurrentRevision = 1;

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

    /// <summary>A zone definition holding a placement of reusable content.</summary>
    /// <param name="key">The zone key.</param>
    /// <param name="allowedTypes">
    /// Block type keys the placement may be shaped by, or empty to allow any. Written as the field
    /// type's captured configuration, which is where the publish check reads it from.
    /// </param>
    public static Zone ReusableZone(string key, params string[] allowedTypes) =>
        new()
        {
            Key = key,
            Name = key,
            FieldTypeKey = FieldTypeKeys.Reusable,
            ConfigurationJson = allowedTypes.Length == 0
                ? null
                : $$"""{"allowedTypes":[{{string.Join(',', allowedTypes.Select(type => $"\"{type}\""))}}]}""",
        };

    /// <summary>
    /// The seeded built-in <c>rawHtml</c> block type, which is every reusable fixture's shape.
    /// </summary>
    /// <remarks>
    /// Read from the database rather than assumed to be id 1: the seed states an id, but a test that
    /// hard-coded it would fail silently and confusingly the first time seeding changes.
    /// </remarks>
    public Task<BlockType> RawHtmlBlockTypeAsync(CancellationToken cancellationToken) =>
        Context.BlockTypes
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Key == CmsSeedData.RawHtmlBlockTypeKey, cancellationToken);

    /// <summary>Creates a reusable item through the real service, failing loudly if it was refused.</summary>
    /// <param name="name">Display name; the key is generated from it.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="blockTypeId">Shape, defaulting to the built-in <c>rawHtml</c> type.</param>
    public async Task<ReusableContentDetail> AddReusableAsync(
        string name,
        CancellationToken cancellationToken,
        int? blockTypeId = null)
    {
        var shape = blockTypeId ?? (await RawHtmlBlockTypeAsync(cancellationToken)).Id;

        var result = await Resolve<IReusableContentService>().CreateAsync(
            new CreateReusableContentRequest(shape, name),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        return result.Value!;
    }

    /// <summary>Writes an HTML fragment into a <c>rawHtml</c> item's draft.</summary>
    /// <param name="item">The item as it currently stands, for its row version.</param>
    /// <param name="html">The fragment to store.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <returns>The item reloaded, so a caller can chain another write.</returns>
    public async Task<ReusableContentDetail> SetReusableHtmlAsync(
        ReusableContentDetail item,
        string html,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);

        var reusable = Resolve<IReusableContentService>();

        var payload = $$"""
            { "schemaVersion": 1, "templateKey": "{{item.Summary.BlockTypeKey}}",
              "templateRevision": {{item.BlockTypeRevision}},
              "zones": { "{{CmsSeedData.RawHtmlContentPropertyKey}}":
                { "type": "html", "value": {{JsonSerializer.Serialize(html)}} } } }
            """;

        var saved = await reusable.SaveDraftAsync(
            item.Summary.Id,
            new SaveDraftRequest(payload, item.RowVersion),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));

        var reloaded = await reusable.GetAsync(item.Summary.Id, cancellationToken);

        return reloaded.Value!;
    }

    /// <summary>Publishes a reusable item, acknowledging the blast radius as a person would.</summary>
    /// <param name="reusableContentId">Identity of the item.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    public async Task<ReusablePublishResult> PublishReusableAsync(
        int reusableContentId,
        CancellationToken cancellationToken)
    {
        var result = await Resolve<IReusableContentService>().PublishAsync(
            reusableContentId,
            acknowledgeWarnings: true,
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        return result.Value!;
    }

    /// <summary>Puts a placement of a reusable item into a page's draft zone.</summary>
    /// <param name="page">The page as it currently stands, for its row version.</param>
    /// <param name="zoneKey">The zone to fill.</param>
    /// <param name="reusableContentId">The item to place.</param>
    /// <param name="cancellationToken">Token observed while saving.</param>
    /// <param name="pinnedVersionId">
    /// The version to pin to, or null for late binding — the default, and the one that delivers G4.
    /// </param>
    /// <returns>The page reloaded.</returns>
    public async Task<PageDetail> PlaceReusableAsync(
        PageDetail page,
        string zoneKey,
        int reusableContentId,
        CancellationToken cancellationToken,
        int? pinnedVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        var pinned = pinnedVersionId is null ? "null" : pinnedVersionId.Value.ToString();

        var payload = $$"""
            { "schemaVersion": 1, "templateKey": "{{page.Summary.TemplateKey}}",
              "templateRevision": {{page.TemplateRevision}},
              "zones": { "{{zoneKey}}":
                { "type": "reusable", "reusableContentId": {{reusableContentId}},
                  "pinnedVersionId": {{pinned}} } } }
            """;

        var saved = await Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(payload, page.RowVersion),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));

        return (await Resolve<IPageService>().GetAsync(page.Summary.Id, cancellationToken)).Value!;
    }

    /// <summary>Flattens a result's diagnostics into an assertion message.</summary>
    public static string Because<T>(CmsResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
    }
}

/// <summary>
/// A clock the test moves by hand.
/// </summary>
/// <remarks>
/// Hand-rolled rather than pulled from <c>Microsoft.Extensions.TimeProvider.Testing</c>: the two
/// things these suites need from it are "what time is it" and "move forward", and adding a package
/// to central package management for that is more moving parts than the code it replaces.
/// <para>
/// <b>It starts at the real current instant rather than at a fixed date, and that is not a
/// convenience.</b> A hard-coded start drifts further from the wall clock every day the repository
/// exists, so any comparison between a stored timestamp and this clock silently changes meaning
/// with the calendar — a suite that is green on the day it is written and red some weeks later,
/// with nothing in between to blame. That is exactly what happened to
/// <c>RetentionKeepsWhatAnEditorWouldBeUpsetToLose</c>, which passed until 2026-08-15 09:00 UTC and
/// failed from that minute on. Starting from now keeps the offset at zero on every run.
/// </para>
/// <para>
/// Tests must therefore only ever assert on time <em>relatively</em> — advance the clock and check
/// what changed. Nothing here may be pinned to a literal date, because there no longer is one.
/// </para>
/// </remarks>
public sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    /// <param name="delta">How far.</param>
    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
