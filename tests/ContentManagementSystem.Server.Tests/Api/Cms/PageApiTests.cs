using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The page read and draft-write API (task P2-16), driven end to end against a real database.
/// </summary>
/// <remarks>
/// The service-layer suites in <c>Content/</c> already assert what these operations do. What is
/// under test here is the part only an endpoint can get wrong: the status code, the precondition,
/// the header, and whether the permission on the route agrees with the check inside the service.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PageApiTests(SqlServerFixture fixture)
{
    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Test]
    public async Task CreatingAPageAnswers201WithItsLocationAndAnEmptyDraft()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-create", cancellationToken);

        var response = await client.PostAsJsonAsync(
            Pages,
            new CreatePageRequest(template.Id, "Pricing"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = (await response.Content.ReadFromJsonAsync<PageDetail>(cancellationToken))!;

        created.Summary.Title.Should().Be("Pricing");
        created.Summary.Slug.Should().Be("pricing");
        created.Summary.DraftVersionNumber.Should().Be(1);
        created.Summary.PublishedVersionNumber.Should().BeNull();
        response.Headers.Location!.ToString().Should().EndWith($"{Pages}/{created.Summary.Id}");

        // Empty means every zone absent, not present-and-null. A required zone blocks a publish and
        // never a create (acceptance criterion P2 #1).
        using var payload = JsonDocument.Parse(created.ContentJson);
        payload.RootElement.GetProperty("templateKey").GetString().Should().Be(template.Key);
        payload.RootElement.GetProperty("zones").EnumerateObject().Should().BeEmpty();
    }

    [Test]
    public async Task ReadingAPageStampsTheDraftsRowVersionAsAnETag()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-etag", cancellationToken);
        var page = await CreatePageAsync(client, template, "Etag", cancellationToken);

        var response = await client.GetAsync($"{Pages}/{page.Summary.Id}", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The tag is the value the draft save has to echo back, so it must be exactly the token the
        // body carries — a client should never have to derive one from the other.
        var body = (await response.Content.ReadFromJsonAsync<PageDetail>(cancellationToken))!;

        response.Headers.ETag!.Tag.Should().Be($"\"{body.RowVersion}\"");
        response.Headers.ETag.IsWeak.Should().BeFalse();
    }

    [Test]
    public async Task ADraftSaveWithNoPreconditionIsRefusedWith428()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-no-precondition", cancellationToken);
        var page = await CreatePageAsync(client, template, "Unconditional", cancellationToken);

        var response = await SaveDraftAsync(
            client,
            page.Summary.Id,
            Payload(template.Key, page.TemplateRevision, "body", "Hello"),
            rowVersion: null,
            cancellationToken);

        // An unconditional draft save is a lost update waiting for two editors, so the server
        // insists rather than accepting one and hoping.
        response.StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);
        (await CodesAsync(response, cancellationToken)).Should().Contain(PageCodes.ConcurrentChange);
    }

    [Test]
    public async Task TwoConcurrentDraftSavesGiveTheSecondA409CarryingBothPayloads()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-conflict", cancellationToken);
        var page = await CreatePageAsync(client, template, "Contested", cancellationToken);

        // Both editors opened the page at the same moment, so both hold this token.
        var opened = page.RowVersion;

        var first = await SaveDraftAsync(
            client,
            page.Summary.Id,
            Payload(template.Key, page.TemplateRevision, "body", "Mine"),
            opened,
            cancellationToken);

        var second = await SaveDraftAsync(
            client,
            page.Summary.Id,
            Payload(template.Key, page.TemplateRevision, "body", "Theirs"),
            opened,
            cancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Acceptance criterion P2 #8, at its literal status code. 409 rather than 412 because the
        // losing editor needs the winning draft in hand to be offered keep-mine or take-theirs, and
        // a 412 is conventionally bodiless.
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // And "both payloads" is literal: the winning draft comes back inside the refusal, so the
        // losing editor's dialog can offer keep-mine, take-theirs, or a diff without a second round
        // trip that would race the same way (task P6-19).
        var problem = await ProblemAsync(second, cancellationToken);

        problem.Errors.Select(error => error.Code).Should().Contain(PageCodes.ConcurrentChange);
        problem.Conflict.Should().NotBeNull();

        var won = problem.Conflict!.Value.GetProperty("draft").Deserialize<DraftState>(
            JsonSerializerOptions.Web)!;

        won.ContentJson.Should().Contain("Mine").And.NotContain("Theirs");
        won.RowVersion.Should().NotBe(opened, "the token in hand is the one that beat this save");

        var stored = await client.GetFromJsonAsync<DraftState>(
            $"{Pages}/{page.Summary.Id}/draft",
            cancellationToken);

        stored!.ContentJson.Should().Contain("Mine").And.NotContain("Theirs");
        stored.RowVersion.Should().Be(won.RowVersion);
    }

    [Test]
    public async Task ARefusalThatNothingWonCarriesNoConflictAtAll()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-no-conflict-body", cancellationToken);
        var page = await CreatePageAsync(client, template, "Malformed", cancellationToken);

        var response = await SaveDraftAsync(
            client,
            page.Summary.Id,
            "not a payload at all",
            page.RowVersion,
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Absent rather than null: "the server told me what it holds" and "the server said nothing"
        // are different answers, and a client that cannot tell them apart would open a conflict
        // dialog over a typo.
        (await ProblemAsync(response, cancellationToken)).Conflict.Should().BeNull();
    }

    [Test]
    public async Task AnUnsavedPayloadCanBeComparedAgainstTheStoredDraft()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-draft-diff", cancellationToken);
        var page = await CreatePageAsync(client, template, "Contested", cancellationToken);

        (await FillZoneAsync(client, page.Summary.Id, "body", "Theirs", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/draft/diff",
            new DiffDraftRequest(Payload(template.Key, page.TemplateRevision, "body", "Mine")),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var diff = (await response.Content.ReadFromJsonAsync<ContentDiff>(cancellationToken))!;

        // The stored draft is the earlier side, because the question a losing editor asks is "what
        // would mine change" — reversing the sides answers the opposite question in the same words.
        var zone = diff.Zones.Should().ContainSingle().Subject;

        zone.ZoneKey.Should().Be("body");
        zone.Kind.Should().Be(ContentChangeKind.Changed);
        zone.Before.Should().Be("Theirs");
        zone.After.Should().Be("Mine");

        // Metadata is deliberately empty: a payload was sent, not a version, and reporting a title
        // as unchanged would be a claim about a value this comparison never saw.
        diff.Metadata.Should().BeEmpty();
    }

    [Test]
    public async Task ComparingAgainstAPageThatDoesNotExistIsANotFoundRatherThanAnEmptyDiff()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Pages}/987654/draft/diff",
            new DiffDraftRequest(Payload("whatever", 1, "body", "Mine")),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task TheSignedInEditorCanReadTheirOwnIdentity()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var me = await client.GetFromJsonAsync<CurrentUser>(
            $"{CmsApiEndpoints.BasePath}/me",
            cancellationToken);

        // The one fact the WebAssembly backoffice cannot get from its serialized authentication
        // state, and the one the properties panel writes into OwnerUserId (task P6-17).
        me!.UserId.Should().Be(1);
        me.DisplayName.Should().Be("test-user-1");
    }

    [Test]
    public async Task NobodySignedInLearnsNothingAboutWhoIsSignedIn()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"{CmsApiEndpoints.BasePath}/me", cancellationToken);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ASavedDraftAnswersWithTheNewETagAndNoNewVersionRow()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-save", cancellationToken);
        var page = await CreatePageAsync(client, template, "Saved", cancellationToken);

        var response = await FillZoneAsync(client, page.Summary.Id, "body", "First words", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var saved = (await response.Content.ReadFromJsonAsync<DraftSaveResult>(cancellationToken))!;

        saved.Draft.VersionNumber.Should().Be(1);
        saved.Draft.RowVersion.Should().NotBe(page.RowVersion);
        response.Headers.ETag!.Tag.Should().Be($"\"{saved.Draft.RowVersion}\"");

        // Acceptance criterion P2 #2, through the endpoint: the draft is mutated in place.
        var versions = await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        versions.Should().ContainSingle();
    }

    [Test]
    public async Task AMetadataPatchChangesOnlyWhatItNames()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-patch", cancellationToken);
        var page = await CreatePageAsync(client, template, "Before", cancellationToken);

        await client.PatchAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/metadata",
            new PatchPageMetadataRequest { MetaDescription = "What our plans cost." },
            cancellationToken);

        var response = await client.PatchAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/metadata",
            new PatchPageMetadataRequest { Title = "After" },
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = (await response.Content.ReadFromJsonAsync<PageDetail>(cancellationToken))!;

        patched.Summary.Title.Should().Be("After");
        // The description survives a patch that never mentioned it, which is the whole reason the
        // request is built out of Patch<T> rather than nullables.
        patched.Seo.MetaDescription.Should().Be("What our plans cost.");
    }

    [Test]
    public async Task AMetadataPatchWithAStalePreconditionIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-patch-conflict", cancellationToken);
        var page = await CreatePageAsync(client, template, "Contested", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "Moved on", cancellationToken);

        using var request = new HttpRequestMessage(
            HttpMethod.Patch,
            $"{Pages}/{page.Summary.Id}/metadata")
        {
            Content = JsonContent.Create(new PatchPageMetadataRequest { Title = "Late" }),
        };

        request.Headers.TryAddWithoutValidation("If-Match", $"\"{page.RowVersion}\"");

        var response = await client.SendAsync(request, cancellationToken);

        // Optional on this route, but honoured when stated: a caller that read first and says so
        // gets the same database-arbitrated check the draft save gets.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Test]
    public async Task TheListIsFilteredAndPagedByCursor()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-list", cancellationToken);
        var other = await CreateTemplateAsync(client, "page-list-other", cancellationToken);

        foreach (var title in new[] { "Alpha", "Beta", "Gamma" })
        {
            await CreatePageAsync(client, template, title, cancellationToken);
        }

        await CreatePageAsync(client, other, "Elsewhere", cancellationToken);

        var first = await client.GetFromJsonAsync<CursorPage<PageSummary>>(
            $"{Pages}?templateId={template.Id}&limit=2",
            cancellationToken);

        first!.Items.Should().HaveCount(2);
        first.NextCursor.Should().NotBeNull();

        var second = await client.GetFromJsonAsync<CursorPage<PageSummary>>(
            $"{Pages}?templateId={template.Id}&limit=2&cursor={first.NextCursor}",
            cancellationToken);

        second!.Items.Should().ContainSingle();
        // Exhausted, so there is nothing after it — which a client learns from the null cursor and
        // not by comparing the count against the limit it asked for.
        second.NextCursor.Should().BeNull();

        first.Items.Concat(second.Items).Select(page => page.Title)
            .Should().Equal("Alpha", "Beta", "Gamma");
    }

    [Test]
    public async Task TheListSearchesTitlesAndRefusesAFilterItCannotRead()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-search", cancellationToken);
        await CreatePageAsync(client, template, "Quarterly Report", cancellationToken);
        await CreatePageAsync(client, template, "Annual Review", cancellationToken);

        var matched = await client.GetFromJsonAsync<CursorPage<PageSummary>>(
            $"{Pages}?templateId={template.Id}&q=quarter",
            cancellationToken);

        var badStatus = await client.GetAsync($"{Pages}?status=Sideways", cancellationToken);
        var badCursor = await client.GetAsync($"{Pages}?cursor=not-a-cursor%21", cancellationToken);

        matched!.Items.Should().ContainSingle().Which.Title.Should().Be("Quarterly Report");

        // Refused rather than ignored. A filter the server silently drops answers with a superset of
        // what was asked for, and the caller has no way to tell that from an honest answer.
        badStatus.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(badStatus, cancellationToken)).Should().Contain(PageCodes.FilterInvalid);
        badCursor.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Test]
    public async Task TheTreeReturnsChildrenToTheRequestedDepth()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-tree", cancellationToken);

        var root = await CreatePageAsync(client, template, "Products", cancellationToken);
        var child = await CreatePageAsync(client, template, "Widgets", cancellationToken, root.Summary.Id);
        await CreatePageAsync(client, template, "Blue Widget", cancellationToken, child.Summary.Id);

        var shallow = await client.GetFromJsonAsync<List<PageTreeNode>>(
            $"{Pages}/tree?parentId={root.Summary.Id}&depth=1",
            cancellationToken);

        var deep = await client.GetFromJsonAsync<List<PageTreeNode>>(
            $"{Pages}/tree?parentId={root.Summary.Id}&depth=2",
            cancellationToken);

        shallow.Should().ContainSingle();
        shallow![0].Page.Title.Should().Be("Widgets");
        // Stopped at, not a leaf. HasChildren is how the expander tells the two apart, and it is why
        // the summary carries the flag at all.
        shallow[0].Children.Should().BeEmpty();
        shallow[0].Page.HasChildren.Should().BeTrue();

        deep![0].Children.Should().ContainSingle()
            .Which.Page.Title.Should().Be("Blue Widget");
    }

    [Test]
    public async Task ADraftIsDiscardedBackToWhatIsPublished()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-discard", cancellationToken);
        var page = await CreatePageAsync(client, template, "Discardable", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "Published words", cancellationToken);
        await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);
        await FillZoneAsync(client, page.Summary.Id, "body", "Regrettable words", cancellationToken);

        var response = await client.PostAsync($"{Pages}/{page.Summary.Id}/draft/discard", null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var draft = (await response.Content.ReadFromJsonAsync<DraftState>(cancellationToken))!;

        draft.ContentJson.Should().Contain("Published words").And.NotContain("Regrettable");
        // A copy, not a repointing: the draft keeps its own row and its own number, or it would be
        // the published row and mutable the moment somebody typed into it.
        draft.VersionNumber.Should().Be(1);
    }

    [Test]
    public async Task ACheckpointAddsAFrozenVersionBesideTheDraft()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-checkpoint", cancellationToken);
        var page = await CreatePageAsync(client, template, "Bookmarked", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "Before the rewrite", cancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/draft/checkpoint",
            new CheckpointDraftRequest("before the big rewrite"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var versions = await client.GetFromJsonAsync<List<PageVersionSummary>>(
            $"{Pages}/{page.Summary.Id}/versions",
            cancellationToken);

        versions.Should().HaveCount(2);
        versions!.Should().ContainSingle(version => version.Label == "before the big rewrite");
        versions.Should().ContainSingle(version => version.IsDraft);
    }

    [Test]
    public async Task AViewerMayReadPagesButNotWriteThem()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var administrator = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(administrator, "page-viewer", cancellationToken);
        var page = await CreatePageAsync(administrator, template, "Readable", cancellationToken);

        using var viewer = await ClientAsync(_factory, cancellationToken, CmsRoles.Viewer);

        var read = await viewer.GetAsync($"{Pages}/{page.Summary.Id}", cancellationToken);
        var list = await viewer.GetAsync(Pages, cancellationToken);
        var create = await viewer.PostAsJsonAsync(
            Pages,
            new CreatePageRequest(template.Id, "Sneaky"),
            cancellationToken);
        var patch = await viewer.PatchAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/metadata",
            new PatchPageMetadataRequest { Title = "Sneaky" },
            cancellationToken);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnAnonymousCallerIsChallengedRatherThanRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var anonymous = _factory.CreateClient();

        var response = await anonymous.GetAsync(Pages, cancellationToken);

        // 401 and not 403: the two mean different things to a client, and the group's floor is an
        // authenticated user rather than any particular permission.
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AWriteWithoutAnAntiforgeryTokenIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var administrator = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(administrator, "page-forged", cancellationToken);

        // Deliberately not given a token: the management API is cookie-authenticated, so this is the
        // shape a cross-site request forgery arrives in.
        using var forger = _factory.CreateClientAs(CmsRoles.Administrator);

        var response = await forger.PostAsJsonAsync(
            Pages,
            new CreatePageRequest(template.Id, "Forged"),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodesAsync(response, cancellationToken)).Should().Contain("request.antiforgery");
    }

    [Test]
    public async Task APayloadNamingAnotherTemplateIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "page-envelope", cancellationToken);
        var page = await CreatePageAsync(client, template, "Guarded", cancellationToken);

        var response = await FillZoneAsync(
            client,
            page.Summary.Id,
            "body",
            "Whose rules?",
            cancellationToken,
            templateKey: "some-other-template");

        // The envelope is a privilege boundary, not data: a client free to name its own template
        // could pick rules its content happens to satisfy (spec section 20.1, one level deeper).
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(response, cancellationToken)).Should().Contain(PageCodes.TemplateMismatch);
    }

    [Test]
    public async Task APageThatIsNotThereIsNotFound()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);

        var page = await client.GetAsync($"{Pages}/987654", cancellationToken);
        var draft = await client.GetAsync($"{Pages}/987654/draft", cancellationToken);
        var tree = await client.GetAsync($"{Pages}/tree?parentId=987654", cancellationToken);

        page.StatusCode.Should().Be(HttpStatusCode.NotFound);
        draft.StatusCode.Should().Be(HttpStatusCode.NotFound);
        tree.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CodesAsync(page, cancellationToken)).Should().Contain(PageCodes.NotFound);
    }
}
