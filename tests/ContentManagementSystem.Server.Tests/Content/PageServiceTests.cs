using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Creating a page, reading it, and patching its metadata (task P2-07).
/// </summary>
/// <remarks>
/// Driven against a real database because most of what is under test is a database fact: three
/// statements committing as one, a draft pointer that closes a mutual foreign key, a materialized
/// path containing an identity the server assigns, and a slug checked against siblings.
/// <para>
/// The service is constructed directly rather than resolved, so that the caller's permissions are a
/// parameter of the test. The registration itself, and the HTTP surface over it, are asserted by the
/// API suite once <c>P2-16</c> maps the endpoints.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PageServiceTests(SqlServerFixture fixture)
{
    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task CreatingAPageProducesADraftVersionWithAnEmptySchemaValidPayload()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken, RequiredZone("hero"));

        var result = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Our Pricing"),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));

        var detail = result.Value!;
        detail.Summary.Title.Should().Be("Our Pricing");
        detail.Summary.Slug.Should().Be("our-pricing");
        detail.Summary.DraftVersionNumber.Should().Be(1);
        detail.Summary.PublishedVersionNumber.Should().BeNull();
        detail.Summary.Status.Should().Be(nameof(PageVersionStatus.Draft));
        detail.TemplateRevision.Should().Be(template.CurrentRevision);

        var payload = ContentPayload.Parse(detail.ContentJson);
        payload.TemplateKey.Should().Be("landing");
        payload.TemplateRevision.Should().Be(template.CurrentRevision);
        payload.HasZones.Should().BeTrue();
        payload.ZoneKeys.Should().BeEmpty("nothing has been authored yet, so every zone is absent");

        // The criterion's other half, and the one worth checking mechanically: the payload a page
        // starts life with satisfies its own template. The zone is *required*, which is exactly the
        // case that must still save — a required zone blocks a publish, never a draft.
        var report = await Validator(scope, template).ValidateAsync(
            payload,
            ValidationMode.Draft,
            cancellationToken);

        report.HasErrors.Should().BeFalse(string.Join("; ", report.Diagnostics.Select(d => d.Message)));
    }

    [Test]
    public async Task TheCreatingTransactionLeavesThePageItsDraftPointerAndItsPath()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);

        var parent = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Products"), cancellationToken)).Value!;
        var child = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Widget", parent.Summary.Id), cancellationToken)).Value!;

        context.ChangeTracker.Clear();

        var stored = await context.Pages
            .AsNoTracking()
            .SingleAsync(page => page.Id == child.Summary.Id, cancellationToken);

        // The three facts the transaction exists to keep together. A page with any one of them
        // missing is one no editor can open, and no query reports it as broken.
        stored.DraftVersionId.Should().NotBeNull();
        stored.Path.Should().Be($"/{parent.Summary.Id}/{child.Summary.Id}/");
        stored.Depth.Should().Be(1);

        var draft = await context.PageVersions
            .AsNoTracking()
            .SingleAsync(version => version.Id == stored.DraftVersionId, cancellationToken);

        draft.PageId.Should().Be(stored.Id);
        draft.VersionNumber.Should().Be(1);
        draft.Status.Should().Be(PageVersionStatus.Draft);
        stored.PublishedVersionId.Should().BeNull();
    }

    [Test]
    public async Task ASiblingCannotClaimASlugThatIsTakenButAPageUnderAnotherParentCan()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);

        var products = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Products"), cancellationToken)).Value!;
        var services = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Services"), cancellationToken)).Value!;

        await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Overview", products.Summary.Id),
            cancellationToken);

        var collision = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Overview", products.Summary.Id),
            cancellationToken);

        collision.Outcome.Should().Be(CmsOutcome.Conflict);
        collision.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.SlugDuplicate);

        // The check is against siblings, not the whole site: a full URL is its ancestors' slugs
        // joined, so /services/overview and /products/overview are two different addresses.
        var elsewhere = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Overview", services.Summary.Id),
            cancellationToken);

        elsewhere.IsSuccess.Should().BeTrue(Because(elsewhere));
    }

    [Test]
    public async Task AReservedSegmentIsRefusedAtTheRootAndAcceptedBeneathAParent()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);

        var refused = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Admin", Slug: "admin"),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.SlugReserved);

        var parent = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Docs"), cancellationToken)).Value!;

        var accepted = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Admin", parent.Summary.Id, "admin"),
            cancellationToken);

        accepted.IsSuccess.Should().BeTrue(Because(accepted));
    }

    [Test]
    public async Task ATemplateThatIsMissingOrDisabledIsRefusedAsABadValueRatherThanAsANotFound()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var missing = await pages.CreateAsync(new CreatePageRequest(999_999, "Orphan"), cancellationToken);

        // Invalid, not NotFound: the address of the request is the page collection, and a 404 would
        // tell a client the endpoint itself is not there.
        missing.Outcome.Should().Be(CmsOutcome.Invalid);
        missing.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.TemplateNotFound);

        var template = await AddTemplateAsync(context, "retired", cancellationToken);
        template.IsEnabled = false;
        await context.SaveChangesAsync(cancellationToken);

        var disabled = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Still Wanted"),
            cancellationToken);

        disabled.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.TemplateDisabled);
    }

    [Test]
    public async Task APageInTheRecycleBinIsNotAnAvailableParent()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);
        var parent = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Old Section"), cancellationToken)).Value!;

        var stored = await context.Pages.SingleAsync(page => page.Id == parent.Summary.Id, cancellationToken);
        stored.IsDeleted = true;
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();

        var result = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "New Child", parent.Summary.Id),
            cancellationToken);

        result.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.ParentNotFound);
    }

    [Test]
    public async Task ANonAsciiSlugIsStoredAndItsHomographWarningSurvivesTheSave()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);

        var result = await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Привет Мир"),
            cancellationToken);

        // Warnings do not block, and a result judged on "did anything get said" rather than on
        // severity would have refused this (spec section 10.3).
        result.IsSuccess.Should().BeTrue(Because(result));
        result.Value!.Summary.Slug.Should().Be("привет-мир");
        result.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.SlugHomograph);
    }

    [Test]
    public async Task APatchChangesOnlyWhatItSuppliesAndCreatesNoNewVersion()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);
        var owner = await AddUserAsync(context, cancellationToken);
        var created = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Pricing"), cancellationToken)).Value!;

        await pages.PatchMetadataAsync(
            created.Summary.Id,
            new PatchPageMetadataRequest
            {
                MetaDescription = "What our plans cost.",
                OwnerUserId = owner,
                InternalNotes = "Check with finance before publishing.",
                RobotsIndex = false,
            },
            cancellationToken: cancellationToken);

        // A second patch naming one member must leave the first patch's five alone. Binding these to
        // plain nullables instead of Patch<T> is what turns "fix the title" into "clear the SEO".
        var patched = await pages.PatchMetadataAsync(
            created.Summary.Id,
            new PatchPageMetadataRequest { Title = "Pricing and Plans" },
            cancellationToken: cancellationToken);

        patched.IsSuccess.Should().BeTrue(Because(patched));

        var detail = patched.Value!;
        detail.Summary.Title.Should().Be("Pricing and Plans");
        detail.Seo.MetaDescription.Should().Be("What our plans cost.");
        detail.Seo.RobotsIndex.Should().BeFalse();
        detail.OwnerUserId.Should().Be(owner);
        detail.InternalNotes.Should().Be("Check with finance before publishing.");
        detail.Summary.Slug.Should().Be("pricing", "a patch that did not mention the slug cannot move the page");

        // The draft is mutated in place. A metadata edit that cut a version would fill the history
        // an editor reads with entries nobody made a decision about (acceptance criterion P2 #2).
        var versions = await context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == created.Summary.Id)
            .ToListAsync(cancellationToken);

        versions.Should().ContainSingle().Which.VersionNumber.Should().Be(1);
    }

    [Test]
    public async Task SendingAMemberAsNullClearsItWhileOmittingItDoesNot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);
        var owner = await AddUserAsync(context, cancellationToken);
        var created = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Pricing"), cancellationToken)).Value!;

        await pages.PatchMetadataAsync(
            created.Summary.Id,
            new PatchPageMetadataRequest { OwnerUserId = owner, MetaTitle = "Plans and pricing" },
            cancellationToken: cancellationToken);

        var cleared = await pages.PatchMetadataAsync(
            created.Summary.Id,
            new PatchPageMetadataRequest { OwnerUserId = new Patch<int?>(null) },
            cancellationToken: cancellationToken);

        cleared.Value!.OwnerUserId.Should().BeNull("an explicit null is how a value is cleared");
        cleared.Value!.Seo.MetaTitle.Should().Be(
            "Plans and pricing",
            "an omitted member is not a null one");
    }

    [Test]
    public async Task APatchIsCheckedBeforeItIsApplied()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);
        var created = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Pricing"), cancellationToken)).Value!;

        var refused = await pages.PatchMetadataAsync(
            created.Summary.Id,
            new PatchPageMetadataRequest
            {
                Title = "   ",
                Priority = 5m,
                StructuredDataJson = "{ not json",
                UseExplicitUrl = true,
            },
            cancellationToken: cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);

        // Every rule broken, not just the first: an editor fixing one field at a time because the
        // server only reports one is a support ticket about the server.
        refused.Diagnostics.Diagnostics.Select(diagnostic => diagnostic.Code)
            .Should().BeEquivalentTo(
            [
                PageCodes.TitleRequired,
                PageCodes.ExplicitUrlMismatch,
                PageCodes.OutOfRange,
                PageCodes.MalformedJson,
            ]);

        context.ChangeTracker.Clear();
        var stored = await context.PageVersions
            .AsNoTracking()
            .SingleAsync(version => version.PageId == created.Summary.Id, cancellationToken);

        stored.Title.Should().Be("Pricing", "a refused patch changes nothing");
    }

    [Test]
    public async Task ReadingAndWritingBothRequireTheirOwnPermission()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var template = await AddTemplateAsync(context, "landing", cancellationToken);

        var allowed = Service(scope, context);
        var created = (await allowed.CreateAsync(
            new CreatePageRequest(template.Id, "Pricing"), cancellationToken)).Value!;

        // A viewer: reads are fine, writes are not. The endpoint policy is the door and this is the
        // lock, so the check has to hold for a caller that never went through an endpoint at all.
        var viewer = Service(scope, context, [CmsPermissions.ContentRead]);

        (await viewer.GetAsync(created.Summary.Id, cancellationToken)).IsSuccess.Should().BeTrue();
        (await viewer.CreateAsync(new CreatePageRequest(template.Id, "Nope"), cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
        (await viewer.PatchMetadataAsync(
                created.Summary.Id,
                new PatchPageMetadataRequest { Title = "Nope" },
                cancellationToken: cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);

        var stranger = Service(scope, context, []);
        (await stranger.GetAsync(created.Summary.Id, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    [Test]
    public async Task ReadingAPageThatIsNotThereIsNotFound()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var result = await pages.GetAsync(999_999, cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.NotFound);
        result.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.NotFound);
    }

    [Test]
    public async Task AnOwnerWhoDoesNotExistIsRefusedRatherThanLeftToTheForeignKey()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var pages = Service(scope, context);

        var template = await AddTemplateAsync(context, "landing", cancellationToken);
        var created = (await pages.CreateAsync(
            new CreatePageRequest(template.Id, "Pricing"), cancellationToken)).Value!;

        // Without the check this is a constraint violation, which reaches the client as a 500 about
        // a database it should not know exists. The ordinary way to get here is picking someone
        // whose account was removed while the form was open.
        var result = await pages.PatchMetadataAsync(
            created.Summary.Id,
            new PatchPageMetadataRequest { OwnerUserId = 999_999 },
            cancellationToken: cancellationToken);

        result.Outcome.Should().Be(CmsOutcome.Invalid);
        result.Diagnostics.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be(PageCodes.OwnerNotFound);
    }

    /// <summary>Inserts a user for the pages that need an owner.</summary>
    private static async Task<int> AddUserAsync(
        ApplicationDbContext context,
        CancellationToken cancellationToken)
    {
        var name = $"owner-{Guid.NewGuid():N}";
        var user = new User
        {
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            MemberSince = DateTimeOffset.UtcNow,
        };

        context.Users.Add(user);
        await context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }

    /// <summary>Builds the service as an ordinary editor, which most of these tests want.</summary>
    private static PageService Service(IServiceScope scope, ApplicationDbContext context) =>
        Service(scope, context, [CmsPermissions.ContentRead, CmsPermissions.ContentEdit]);

    /// <summary>Builds the service with a caller holding exactly the given permissions.</summary>
    /// <param name="scope">Scope supplying the tree service.</param>
    /// <param name="context">The database context under test.</param>
    /// <param name="permissions">
    /// What the caller holds. An empty array is a caller holding nothing, which is a different case
    /// from the overload's default and is why this is not a <c>params</c> parameter.
    /// </param>
    private static PageService Service(
        IServiceScope scope,
        ApplicationDbContext context,
        string[] permissions) =>
        new(
            context,
            scope.ServiceProvider.GetRequiredService<IPageTreeService>(),
            // Resolved rather than stubbed. It shares this scope's ApplicationDbContext — the same
            // instance the test asserts against — so the route rows it writes are committed by the
            // page service's own SaveChanges, which is the arrangement production runs.
            scope.ServiceProvider.GetRequiredService<IUrlService>(),
            new StubAuthorization(permissions),
            TimeProvider.System,
            NullLogger<PageService>.Instance);

    /// <summary>Builds a validator over the field types this deployment registered.</summary>
    private static ContentSchemaValidator Validator(IServiceScope scope, Template template) =>
        new(
            scope.ServiceProvider.GetRequiredService<IFieldTypeRegistry>(),
            new ContentSchemaCatalog(
                [
                    ContentSchemaSnapshot.ReadTemplate(
                        template.Key,
                        template.CurrentRevision,
                        template.Revisions.Single(revision =>
                            revision.RevisionNumber == template.CurrentRevision).ZoneSnapshotJson),
                ],
                []));

    /// <summary>Inserts a template and its first revision, as the structure API would.</summary>
    private static async Task<Template> AddTemplateAsync(
        ApplicationDbContext context,
        string key,
        CancellationToken cancellationToken,
        params Zone[] zones)
    {
        var template = new Template
        {
            Key = key,
            Name = key,
            CurrentRevision = 1,
            IsEnabled = true,
        };

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

        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        return template;
    }

    private static Zone RequiredZone(string key) =>
        new()
        {
            Key = key,
            Name = key,
            FieldTypeKey = FieldTypeKeys.PlainText,
            IsRequired = true,
        };

    /// <summary>Renders a failed result's diagnostics into the assertion message.</summary>
    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

    /// <summary>Grants exactly the permissions it was given.</summary>
    private sealed class StubAuthorization(string[] permissions) : ICmsAuthorization
    {
        public bool HasPermission(string permission) => permissions.Contains(permission);
    }
}
