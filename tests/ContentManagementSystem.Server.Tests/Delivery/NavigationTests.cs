using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Navigation;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// Navigation, generated and managed, and the cache generation it changes within
/// (tasks P8-15 to P8-17).
/// </summary>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class NavigationTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;
    private Template? _template;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task UnpublishingAPageRemovesItFromEveryOtherPagesNavigation()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", "Prices", cancellationToken);
        var seasonal = await PublishedPageAsync("Seasonal offer", "Only for now", cancellationToken);

        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        (await client.GetStringAsync("/pricing", cancellationToken))
            .Should().Contain("Seasonal offer").And.Contain("/seasonal-offer");

        (await _bench.Resolve<IPublishingService>()
            .UnpublishAsync(seasonal.Summary.Id, cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        await DispatchAsync(cancellationToken);

        // Acceptance criterion P8 #9. The page showing the menu was evicted by the nav tag it took
        // while rendering, so the removal is visible on the *other* page rather than only in a
        // fresh query.
        (await client.GetStringAsync("/pricing", cancellationToken))
            .Should().NotContain("Seasonal offer");
    }

    [Test]
    public async Task APageExcludedFromNavigationIsAbsentEvenWhilePublished()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        await PublishedPageAsync("Pricing", "Prices", cancellationToken);
        var hidden = await PublishedPageAsync("Thank you", "After the form", cancellationToken);

        (await _bench.Resolve<IPageService>().PatchMetadataAsync(
            hidden.Summary.Id,
            new PatchPageMetadataRequest { ShowInNavigation = new Patch<bool>(false) },
            null,
            cancellationToken)).IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        await DispatchAsync(cancellationToken);

        using var client = _bench.CreateClient();

        // Two different switches: "not in the menu" is an editor's decision and "not yet" is the
        // site's. A published page can be either.
        (await client.GetStringAsync("/pricing", cancellationToken)).Should().NotContain("Thank you");
    }

    [Test]
    public async Task AManagedMenuKeepsItsOrderAndDropsItemsWhosePageIsNotPublished()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var published = await PublishedPageAsync("Privacy", "The policy", cancellationToken);
        var template = await TemplateAsync(cancellationToken);
        var draftOnly = await _bench.AddPageAsync(template, "Unreleased", cancellationToken);

        var menu = new NavigationMenu { Key = "footer", Name = "Footer" };

        menu.Items.Add(new NavigationItem
        {
            Label = "Status",
            ExternalUrl = "https://status.example.com",
            OpenInNewTab = true,
            SortOrder = 2,
        });

        menu.Items.Add(new NavigationItem { Label = "Privacy", PageId = published.Summary.Id, SortOrder = 1 });
        menu.Items.Add(new NavigationItem { Label = "Coming soon", PageId = draftOnly.Summary.Id, SortOrder = 3 });

        _bench.Context.NavigationMenus.Add(menu);
        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        await using var scope = _bench.NewScope();
        var nodes = await scope.ServiceProvider.GetRequiredService<INavigationService>()
            .GetMenuAsync("footer", cancellationToken);

        // The unpublished page's entry is dropped rather than rendered as a dead link, which is the
        // failure an editor is least likely to notice.
        nodes.Should().HaveCount(2);
        nodes[0].Label.Should().Be("Privacy");
        nodes[0].Url.Should().Be("/privacy");
        nodes[1].Url.Should().Be("https://status.example.com");
        nodes[1].OpenInNewTab.Should().BeTrue();
    }

    [Test]
    public async Task EditingAMenuEvictsThePagesThatRenderIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PublishedPageAsync("Pricing", "Prices", cancellationToken);

        var menu = new NavigationMenu { Key = "footer", Name = "Footer" };

        menu.Items.Add(new NavigationItem { Label = "Pricing", PageId = page.Summary.Id, SortOrder = 1 });

        _bench.Context.NavigationMenus.Add(menu);
        await _bench.Context.SaveChangesAsync(cancellationToken);
        _bench.Context.ChangeTracker.Clear();

        await using var scope = _bench.NewScope();
        var queue = scope.ServiceProvider.GetRequiredService<ICacheInvalidationQueue>();
        var context = scope.ServiceProvider.GetRequiredService<Data.Models.ApplicationDbContext>();

        await queue.EnqueuePageAsync(page.Summary.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var enqueued = await context.OutboxMessages
            .AsNoTracking()
            .Where(message => message.ProcessedOn == null)
            .Select(message => message.PayloadJson)
            .ToListAsync(cancellationToken);

        // A page named by a managed menu carries that menu's tag into every eviction it causes: the
        // footer renders this page's title, so a page that changes changes every page showing the
        // footer (task P8-17).
        enqueued.Should().Contain(payload => payload.Contains("nav:footer", StringComparison.Ordinal));
    }

    private async Task DispatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _bench.NewScope();

        await scope.ServiceProvider.GetRequiredService<OutboxRunner>().RunOnceAsync(cancellationToken);
    }

    private async Task<Template> TemplateAsync(CancellationToken cancellationToken) =>
        _template ??= await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

    private async Task<PageDetail> PublishedPageAsync(
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        var template = await TemplateAsync(cancellationToken);
        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(Payload(text), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();

        return page;
    }

    private static string Payload(string text) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "{{TemplateKey}}", "templateRevision": 1,
          "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
        """;

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
