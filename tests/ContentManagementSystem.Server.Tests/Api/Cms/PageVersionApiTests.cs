using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The version, diff, and edit-lock API (tasks P2-18 and P2-19).
/// </summary>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PageVersionApiTests(SqlServerFixture fixture)
{
    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task HistoryListsEveryVersionNewestFirstWithItsRole()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "version-history", cancellationToken);
        var page = await CreatePageAsync(client, template, "Historic", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "First", cancellationToken);
        await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);
        await FillZoneAsync(client, page.Summary.Id, "body", "Second", cancellationToken);

        var versions = await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        versions.Should().HaveCount(2);
        versions!.Select(version => version.VersionNumber).Should().BeInDescendingOrder();

        // Acceptance criterion P2 #5. Whether a version is the draft or the live one is resolved
        // against the page's two pointers, not inferred from the status column.
        versions.Should().ContainSingle(version => version.IsDraft);
        versions.Should().ContainSingle(version => version.IsPublished);
        versions.Should().OnlyContain(version => version.CreatedOn != null);
    }

    [Test]
    public async Task OneVersionIsReadableAndAnotherPagesVersionIsNot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "version-read", cancellationToken);
        var page = await CreatePageAsync(client, template, "Readable", cancellationToken);
        var other = await CreatePageAsync(client, template, "Unrelated", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "Stored words", cancellationToken);

        var versions = await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        var versionId = versions![0].Id;

        var mine = await client.GetAsync($"{Pages}/{page.Summary.Id}/versions/{versionId}", cancellationToken);
        var theirs = await client.GetAsync($"{Pages}/{other.Summary.Id}/versions/{versionId}", cancellationToken);

        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        (await mine.Content.ReadFromJsonAsync<PageVersionDetail>(cancellationToken))!
            .ContentJson.Should().Contain("Stored words");

        // The pair is the address. Answering anything else for a version of another page would
        // confirm the existence of a row the caller did not ask about.
        theirs.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheDiffReportsAReorderedBlockAsMovedRatherThanRemovedAndAdded()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateBlocksTemplateAsync(client, "version-diff", cancellationToken);
        var page = await CreatePageAsync(client, template, "Rearranged", cancellationToken);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await SaveBlocksAsync(client, page.Summary.Id, cancellationToken, first, second);
        await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        await SaveBlocksAsync(client, page.Summary.Id, cancellationToken, second, first);

        var versions = await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        var published = versions!.Single(version => version.IsPublished);
        var draft = versions!.Single(version => version.IsDraft);

        var response = await client.GetAsync(
            $"{Pages}/{page.Summary.Id}/versions/{published.Id}/diff/{draft.Id}",
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = (await response.Content.ReadFromJsonAsync<ContentDiff>(cancellationToken))!;

        // Acceptance criterion P2 #6, through the endpoint. Matched on the stable GUID, which is the
        // whole reason the blocks field type writes one.
        var blocks = diff.Zones.SelectMany(zone => zone.Blocks).ToList();

        blocks.Should().HaveCount(2);
        blocks.Should().OnlyContain(block => block.Kind == ContentChangeKind.Moved);
        blocks.Should().Contain(block => block.BlockId == first && block.BeforeIndex == 0 && block.AfterIndex == 1);
    }

    [Test]
    public async Task RestoringAVersionCopiesItIntoTheDraftAndReturnsTheNewETag()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "version-restore", cancellationToken);
        var page = await CreatePageAsync(client, template, "Restorable", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "The good version", cancellationToken);
        await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);
        await FillZoneAsync(client, page.Summary.Id, "body", "The regrettable version", cancellationToken);

        var versions = await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        var published = versions!.Single(version => version.IsPublished);

        var response = await client.PostAsync(
            $"{Pages}/{page.Summary.Id}/versions/{published.Id}/restore",
            null,
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var draft = (await response.Content.ReadFromJsonAsync<DraftState>(cancellationToken))!;

        draft.ContentJson.Should().Contain("The good version");
        // The token the editor was holding is stale the moment a restore returns, so the response
        // carries the new one rather than making the next save lose a race with itself.
        response.Headers.ETag!.Tag.Should().Be($"\"{draft.RowVersion}\"");

        // Acceptance criterion P2 #7: the published version is untouched by a restore.
        var live = await client.GetFromJsonAsync<PageVersionDetail>(
            $"{Pages}/{page.Summary.Id}/versions/{published.Id}",
            cancellationToken);

        live!.ContentJson.Should().Contain("The good version");
        live.Summary.IsPublished.Should().BeTrue();
    }

    [Test]
    public async Task AViewerMayReadHistoryButNotRestoreFromIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var administrator = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(administrator, "version-viewer", cancellationToken);
        var page = await CreatePageAsync(administrator, template, "Guarded", cancellationToken);

        var versions = await administrator.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        using var viewer = await ClientAsync(_factory, cancellationToken, CmsRoles.Viewer);

        var read = await viewer.GetAsync($"{Pages}/{page.Summary.Id}/versions", cancellationToken);
        var restore = await viewer.PostAsync(
            $"{Pages}/{page.Summary.Id}/versions/{versions![0].Id}/restore",
            null,
            cancellationToken);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        restore.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnUnheldPageAnswersNoContentRatherThanNotFound()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "lock-empty", cancellationToken);
        var page = await CreatePageAsync(client, template, "Nobody Home", cancellationToken);

        var response = await client.GetAsync($"{Pages}/{page.Summary.Id}/lock", cancellationToken);

        // The question has been answered and the answer is nobody. A 404 would be
        // indistinguishable from the page itself not existing.
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Test]
    public async Task ALockIsVisibleToASecondEditorAndNeverBlocksTheirSave()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var elena = await AddUserAsync("elena", cancellationToken);
        var marcus = await AddUserAsync("marcus", cancellationToken);

        using var first = await EditorAsync(elena, cancellationToken);
        var template = await CreateTemplateAsync(
            await AdministratorAsync(_factory, cancellationToken),
            "lock-shared",
            cancellationToken);
        var page = await CreatePageAsync(first, template, "Contested", cancellationToken);

        var acquired = await first.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/lock",
            new AcquireLockRequest(),
            cancellationToken);

        acquired.StatusCode.Should().Be(HttpStatusCode.OK);
        (await acquired.Content.ReadFromJsonAsync<EditLockState>(cancellationToken))!
            .IsMine.Should().BeTrue();

        using var second = await EditorAsync(marcus, cancellationToken);

        var seen = await second.GetFromJsonAsync<EditLockState>(
            $"{Pages}/{page.Summary.Id}/lock",
            cancellationToken);

        seen!.IsMine.Should().BeFalse();
        // The banner says who, not which id, so the holder's display name has to survive the trip.
        seen.UserName.Should().Be("elena");

        // The property the whole design turns on: a lock is advisory, so the second editor's write
        // still goes through (ADR 0012).
        var save = await FillZoneAsync(second, page.Summary.Id, "body", "Typed anyway", cancellationToken);

        save.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task ALockCanBeTakenOverAndReleased()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var elena = await AddUserAsync("elena", cancellationToken);
        var marcus = await AddUserAsync("marcus", cancellationToken);

        using var first = await EditorAsync(elena, cancellationToken);
        var template = await CreateTemplateAsync(
            await AdministratorAsync(_factory, cancellationToken),
            "lock-takeover",
            cancellationToken);
        var page = await CreatePageAsync(first, template, "Handed Over", cancellationToken);

        await first.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/lock",
            new AcquireLockRequest(),
            cancellationToken);

        using var second = await EditorAsync(marcus, cancellationToken);

        var polite = await second.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/lock",
            new AcquireLockRequest(),
            cancellationToken);

        var insistent = await second.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/lock",
            new AcquireLockRequest(TakeOver: true),
            cancellationToken);

        // Opening the page reports the holder rather than stealing from them; "Edit anyway" is the
        // explicit second act.
        polite.StatusCode.Should().Be(HttpStatusCode.OK);
        (await polite.Content.ReadFromJsonAsync<EditLockState>(cancellationToken))!.IsMine.Should().BeFalse();

        insistent.StatusCode.Should().Be(HttpStatusCode.OK);
        (await insistent.Content.ReadFromJsonAsync<EditLockState>(cancellationToken))!.IsMine.Should().BeTrue();

        var released = await second.DeleteAsync($"{Pages}/{page.Summary.Id}/lock", cancellationToken);
        var afterwards = await second.GetAsync($"{Pages}/{page.Summary.Id}/lock", cancellationToken);

        released.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterwards.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// A signed-in editor with a real user row behind them.
    /// </summary>
    /// <remarks>
    /// The lock table carries a foreign key to <c>Users</c>, so unlike every other suite here these
    /// tests cannot invent an identity in a header alone — and that constraint is the point, since
    /// the banner an editor sees names the holder rather than their id.
    /// </remarks>
    private async Task<HttpClient> EditorAsync(int userId, CancellationToken cancellationToken)
    {
        var client = _factory.CreateClientAs(CmsRoles.Editor);

        client.DefaultRequestHeaders.Add(
            TestAuthHandler.UserIdHeader,
            userId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return await CmsApplicationFactory.WithAntiforgeryTokenAsync(client, cancellationToken);
    }

    /// <summary>Inserts a user row, so a lock can point at somebody who exists.</summary>
    private async Task<int> AddUserAsync(string name, CancellationToken cancellationToken)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<Data.Models.ApplicationDbContext>();

        var user = new Data.Models.User
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

    /// <summary>Creates a template whose single zone holds blocks, for the diff case.</summary>
    private static async Task<Shared.Contracts.Structure.TemplateSummary> CreateBlocksTemplateAsync(
        HttpClient client,
        string key,
        CancellationToken cancellationToken)
    {
        var created = await client.PostAsJsonAsync(
            Templates,
            new Shared.Contracts.Structure.CreateTemplateRequest(key, key),
            cancellationToken);

        var template = (await created.Content
            .ReadFromJsonAsync<Shared.Contracts.Structure.TemplateDetail>(cancellationToken))!.Template;

        await client.PostAsJsonAsync(
            $"{Templates}/{template.Id}/zones",
            new Shared.Contracts.Structure.CreateZoneRequest("sections", "Sections", FieldTypeKeys.Blocks),
            cancellationToken);

        return template with { CurrentRevision = template.CurrentRevision + 1 };
    }

    /// <summary>Saves a blocks zone holding the given block ids, in order.</summary>
    /// <remarks>
    /// Every block is the seeded built-in <c>rawHtml</c> type, which every deployment ships with, so
    /// the arrange step needs no block type of its own — what the diff is being asked about is the
    /// order, not the contents.
    /// </remarks>
    private static async Task SaveBlocksAsync(
        HttpClient client,
        int pageId,
        CancellationToken cancellationToken,
        params Guid[] blockIds)
    {
        var page = await client.GetFromJsonAsync<PageDetail>($"{Pages}/{pageId}", cancellationToken);

        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = Shared.Content.ContentPayload.CurrentSchemaVersion,
            templateKey = page!.Summary.TemplateKey,
            templateRevision = page.TemplateRevision,
            zones = new Dictionary<string, object>
            {
                ["sections"] = new
                {
                    type = FieldTypeKeys.Blocks,
                    items = blockIds.Select(id => new
                    {
                        id,
                        blockTypeKey = "rawHtml",
                        blockTypeRevision = 1,
                        properties = new Dictionary<string, object>
                        {
                            ["content"] = new { type = FieldTypeKeys.Html, value = $"<p>{id}</p>" },
                        },
                    }).ToArray(),
                },
            },
        });

        var response = await SaveDraftAsync(client, pageId, payload, page.RowVersion, cancellationToken);

        response.StatusCode.Should().Be(
            HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync(cancellationToken));
    }
}
