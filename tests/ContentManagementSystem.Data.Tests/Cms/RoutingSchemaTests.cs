using System.Security.Cryptography;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.TestSupport;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Data.Tests.Cms;

/// <summary>
/// Asserts the storage guarantees routing depends on (task P3-01, spec sections 10.4 and 23.5).
/// </summary>
/// <remarks>
/// The whole point of the <c>binary(32)</c> hash columns is that a 2000-character URL cannot carry
/// an index key of its own, so every uniqueness rule here is enforced on a value nothing writes
/// directly. Asserted against real SQL Server because a filtered unique index has no faithful
/// in-memory equivalent — and the filter is exactly what makes a draft route legal beside a live one.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class RoutingSchemaTests(SqlServerFixture fixture)
{
    [Test]
    public async Task TwoPublishedRoutesCannotShareAUrl()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var first = await CreatePageAsync(context, "one", cancellationToken);
        var second = await CreatePageAsync(context, "two", cancellationToken);

        context.PageRoutes.Add(Route(first.Id, "/shared", isPublished: true));
        await context.SaveChangesAsync(cancellationToken);

        context.PageRoutes.Add(Route(second.Id, "/shared", isPublished: true));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task ADraftRouteMaySitAtAUrlAPublishedRouteAlreadyHolds()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var live = await CreatePageAsync(context, "live", cancellationToken);
        var replacement = await CreatePageAsync(context, "replacement", cancellationToken);

        context.PageRoutes.Add(Route(live.Id, "/handbook", isPublished: true));
        context.PageRoutes.Add(Route(replacement.Id, "/handbook", isPublished: false));

        // The index is filtered on IsPublished = 1 precisely so this is allowed. Preparing a
        // replacement at the URL a live page still serves is ordinary work, and a plain unique index
        // here is the standard CMS schema trap.
        await context.SaveChangesAsync(cancellationToken);

        (await context.PageRoutes.CountAsync(route => route.Url == "/handbook", cancellationToken))
            .Should().Be(2);
    }

    [Test]
    public async Task DeletingAPageTakesItsRoutesAndItsPreviewTokens()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "temporary", cancellationToken);

        context.PageRoutes.Add(Route(page.Id, "/temporary", isPublished: false));
        context.PreviewTokens.Add(new PreviewToken
        {
            TokenHash = RandomNumberGenerator.GetBytes(SiteUrls.HashLength),
            PageId = page.Id,
            PageVersionId = page.DraftVersionId!.Value,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
        });

        await context.SaveChangesAsync(cancellationToken);

        // Routes and tokens are derived data with no life of their own, so both cascade. Restrict
        // here would make a permanent delete fail on rows the purge would only have had to remove.
        await context.Pages
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == page.Id)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(candidate => candidate.DraftVersionId, (int?)null)
                    .SetProperty(candidate => candidate.PublishedVersionId, (int?)null),
                cancellationToken);

        await context.PreviewTokens.Where(token => token.PageId == page.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.PageVersions.Where(version => version.PageId == page.Id)
            .ExecuteDeleteAsync(cancellationToken);
        await context.Pages.IgnoreQueryFilters().Where(candidate => candidate.Id == page.Id)
            .ExecuteDeleteAsync(cancellationToken);

        (await context.PageRoutes.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Test]
    public async Task TwoRedirectsCannotShareASourceUrlEvenWhenOneIsDisabled()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        context.Redirects.Add(Redirect("/legacy", "/current", isEnabled: false));
        await context.SaveChangesAsync(cancellationToken);

        // Unfiltered, unlike the route index: a disabled redirect still occupies its source, so
        // re-enabling it must not become a constraint violation waiting to happen.
        context.Redirects.Add(Redirect("/legacy", "/somewhere-else", isEnabled: true));

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task ARedirectPointingAtAPageBlocksThatPagesDeletion()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "target", cancellationToken);

        var redirect = Redirect("/old", toUrl: null, isEnabled: true);
        redirect.ToPageId = page.Id;
        context.Redirects.Add(redirect);
        await context.SaveChangesAsync(cancellationToken);

        // Restrict rather than cascade, so a missed rewrite in the purge path is a loud failure
        // rather than an administrator's rule silently disappearing.
        await context.Pages
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == page.Id)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(candidate => candidate.DraftVersionId, (int?)null),
                cancellationToken);

        await context.PageVersions.Where(version => version.PageId == page.Id)
            .ExecuteDeleteAsync(cancellationToken);

        var purge = async () => await context.Pages
            .IgnoreQueryFilters()
            .Where(candidate => candidate.Id == page.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await purge.Should().ThrowAsync<SqlException>();
    }

    [Test]
    public async Task OneUrlProducesOneNotFoundRow()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var now = DateTimeOffset.UtcNow;

        context.NotFoundLogs.Add(new NotFoundLog
        {
            Url = "/missing",
            UrlHash = SiteUrls.Hash("/missing"),
            HitCount = 1,
            FirstSeenOn = now,
            LastSeenOn = now,
        });

        await context.SaveChangesAsync(cancellationToken);

        // The uniqueness is what makes the writer an upsert rather than an append, and it is the
        // whole reason a crawler cannot make this the largest table on the site.
        context.NotFoundLogs.Add(new NotFoundLog
        {
            Url = "/missing",
            UrlHash = SiteUrls.Hash("/missing"),
            HitCount = 1,
            FirstSeenOn = now,
            LastSeenOn = now,
        });

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task TwoPreviewTokensCannotShareAHash()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var context = await fixture.CreateDatabaseAsync(cancellationToken: cancellationToken);

        var page = await CreatePageAsync(context, "draft", cancellationToken);
        var hash = RandomNumberGenerator.GetBytes(SiteUrls.HashLength);

        context.PreviewTokens.Add(new PreviewToken
        {
            TokenHash = hash,
            PageId = page.Id,
            PageVersionId = page.DraftVersionId!.Value,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
        });

        await context.SaveChangesAsync(cancellationToken);

        context.PreviewTokens.Add(new PreviewToken
        {
            TokenHash = hash,
            PageId = page.Id,
            PageVersionId = page.DraftVersionId.Value,
            ExpiresOn = DateTimeOffset.UtcNow.AddDays(7),
        });

        var save = async () => await context.SaveChangesAsync(cancellationToken);
        await save.Should().ThrowAsync<DbUpdateException>();
    }

    /// <summary>Builds a route row with its hash derived, as every writer must.</summary>
    private static PageRoute Route(int pageId, string url, bool isPublished) =>
        new()
        {
            PageId = pageId,
            Url = url,
            UrlHash = SiteUrls.Hash(url),
            IsPrimary = true,
            IsPublished = isPublished,
            CreatedOn = DateTimeOffset.UtcNow,
        };

    /// <summary>Builds a redirect row with its hash derived.</summary>
    private static Redirect Redirect(string fromUrl, string? toUrl, bool isEnabled) =>
        new()
        {
            FromUrl = fromUrl,
            FromUrlHash = SiteUrls.Hash(fromUrl),
            ToUrl = toUrl,
            StatusCode = 301,
            IsEnabled = isEnabled,
        };

    /// <summary>Inserts a page and its first draft, closing the mutual foreign key.</summary>
    private static async Task<Page> CreatePageAsync(
        ApplicationDbContext context,
        string slug,
        CancellationToken cancellationToken)
    {
        var template = new Template
        {
            Key = $"template-{Guid.NewGuid():N}",
            Name = "Landing page",
            CurrentRevision = 1,
        };

        context.Templates.Add(template);
        await context.SaveChangesAsync(cancellationToken);

        var page = new Page
        {
            PublicId = Guid.NewGuid(),
            Slug = slug,
            Path = "/",
            TemplateId = template.Id,
        };

        context.Pages.Add(page);
        await context.SaveChangesAsync(cancellationToken);

        page.Path = $"/{page.Id}/";

        var draft = new PageVersion
        {
            PageId = page.Id,
            VersionNumber = 1,
            Status = PageVersionStatus.Draft,
            Title = slug,
            ContentJson = "{}",
            TemplateId = page.TemplateId,
            TemplateRevision = 1,
        };

        context.PageVersions.Add(draft);
        await context.SaveChangesAsync(cancellationToken);

        page.DraftVersionId = draft.Id;
        await context.SaveChangesAsync(cancellationToken);

        return page;
    }
}
