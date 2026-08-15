using System.Net;
using System.Net.Http.Json;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using static ContentManagementSystem.Server.Tests.Api.Cms.PageApiClient;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// The lifecycle API — publish, unpublish, duplicate, and the recycle bin (task P2-17).
/// </summary>
/// <remarks>
/// Every operation here changes what the public site serves or what exists at all, so each is
/// asserted at its literal status code and against the permission on its route. Two of Phase 2's
/// acceptance criteria were left at <c>[~]</c> waiting for exactly these endpoints: P2 #8's
/// <c>409</c> is in the draft suite, and P2 #11's <c>422</c> is here.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class PageLifecycleApiTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task PublishingWithAnUnfilledRequiredZoneAnswers422NamingThatZone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(
            client,
            "lifecycle-required",
            cancellationToken,
            zoneKey: "headline",
            required: true);

        var page = await CreatePageAsync(client, template, "Unfinished", cancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        // Acceptance criterion P2 #11, at its literal status code.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var problem = await ProblemAsync(response, cancellationToken);

        problem.Errors.Should().NotBeEmpty();
        // Named, not merely counted: the editor has to be shown which zone to go and fill in.
        problem.Errors.Should().Contain(error =>
            error.Property != null && error.Property.Contains("headline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidatingRunsTheSameChecksWithoutPublishing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(
            client,
            "lifecycle-validate",
            cancellationToken,
            zoneKey: "headline",
            required: true);

        var page = await CreatePageAsync(client, template, "Checkable", cancellationToken);

        var before = await client.PostAsync($"{Pages}/{page.Summary.Id}/validate", null, cancellationToken);
        var blocked = (await before.Content.ReadFromJsonAsync<PublishValidation>(cancellationToken))!;

        await FillZoneAsync(client, page.Summary.Id, "headline", "Now filled in", cancellationToken);

        var after = await client.PostAsync($"{Pages}/{page.Summary.Id}/validate", null, cancellationToken);
        var clear = (await after.Content.ReadFromJsonAsync<PublishValidation>(cancellationToken))!;

        // A dry run answers 200 whatever it finds: the request itself succeeded, and what it found
        // is the body. Only an actual publish turns those errors into a refusal.
        before.StatusCode.Should().Be(HttpStatusCode.OK);
        blocked.CanPublish.Should().BeFalse();
        blocked.Errors.Should().NotBeEmpty();

        after.StatusCode.Should().Be(HttpStatusCode.OK);
        clear.CanPublish.Should().BeTrue();
        clear.Errors.Should().BeEmpty();

        // Nothing was published by either check.
        var reread = await client.GetFromJsonAsync<PageDetail>($"{Pages}/{page.Summary.Id}", cancellationToken);
        reread!.Summary.PublishedVersionNumber.Should().BeNull();
    }

    [Fact]
    public async Task PublishingCreatesANewVersionAndLeavesTheDraftEditable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "lifecycle-publish", cancellationToken);
        var page = await CreatePageAsync(client, template, "Publishable", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "Live words", cancellationToken);

        var response = await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var published = (await response.Content.ReadFromJsonAsync<PublishResult>(cancellationToken))!;

        published.VersionNumber.Should().Be(2);
        published.ArchivedVersionNumber.Should().BeNull();

        // Acceptance criterion P2 #4 through the endpoints: the draft moves on and the published
        // version does not.
        await FillZoneAsync(client, page.Summary.Id, "body", "Draft words", cancellationToken);

        var live = await client.GetFromJsonAsync<PageVersionDetail>(
            $"{Pages}/{page.Summary.Id}/versions/{published.VersionId}",
            cancellationToken);

        live!.ContentJson.Should().Contain("Live words").And.NotContain("Draft words");
    }

    [Fact]
    public async Task UnpublishingRetiresTheLiveVersionAndSaysWhichOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "lifecycle-unpublish", cancellationToken);
        var page = await CreatePageAsync(client, template, "Retirable", cancellationToken);

        await FillZoneAsync(client, page.Summary.Id, "body", "Briefly live", cancellationToken);
        await client.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        var response = await client.PostAsync($"{Pages}/{page.Summary.Id}/unpublish", null, cancellationToken);
        var again = await client.PostAsync($"{Pages}/{page.Summary.Id}/unpublish", null, cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<UnpublishResult>(cancellationToken))!
            .UnpublishedVersionNumber.Should().Be(2);

        // Idempotent in outcome but honest about it: nothing is live, so there is nothing to retire.
        again.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(again, cancellationToken)).Should().Contain(PageCodes.AlreadyUnpublished);
    }

    [Fact]
    public async Task AnAuthorMayEditButNotPublish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var administrator = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(administrator, "lifecycle-author", cancellationToken);

        using var author = await ClientAsync(_factory, cancellationToken, CmsRoles.Author);

        var page = await CreatePageAsync(author, template, "Authored", cancellationToken);
        var edit = await FillZoneAsync(author, page.Summary.Id, "body", "Written by me", cancellationToken);
        var publish = await author.PostAsJsonAsync(
            $"{Pages}/{page.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);
        var delete = await author.DeleteAsync($"{Pages}/{page.Summary.Id}", cancellationToken);

        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        // Spec section 21.1: an Author writes and somebody else decides what goes live.
        publish.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        delete.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DuplicatingAPageAnswers201WithTheCopy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "lifecycle-duplicate", cancellationToken);
        var original = await CreatePageAsync(client, template, "Campaign", cancellationToken);
        await CreatePageAsync(client, template, "Landing", cancellationToken, original.Summary.Id);
        await FillZoneAsync(client, original.Summary.Id, "body", "Reusable words", cancellationToken);

        var response = await client.PostAsync(
            $"{Pages}/{original.Summary.Id}/duplicate?deep=true",
            null,
            cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var copy = (await response.Content.ReadFromJsonAsync<PageDetail>(cancellationToken))!;

        copy.Summary.Id.Should().NotBe(original.Summary.Id);
        copy.Summary.Title.Should().Contain("copy");
        copy.ContentJson.Should().Contain("Reusable words");
        // A copy starts at version 1 and unpublished, whatever the original was (spec section 14.12).
        copy.Summary.DraftVersionNumber.Should().Be(1);
        copy.Summary.PublishedVersionNumber.Should().BeNull();

        var children = await client.GetFromJsonAsync<List<PageTreeNode>>(
            $"{Pages}/tree?parentId={copy.Summary.Id}",
            cancellationToken);

        children.Should().ContainSingle().Which.Page.Title.Should().Be("Landing");
    }

    [Fact]
    public async Task DeletingTakesTheSubtreeToTheRecycleBinAndRestoringBringsItBackAsDrafts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "lifecycle-bin", cancellationToken);
        var parent = await CreatePageAsync(client, template, "Section", cancellationToken);
        var child = await CreatePageAsync(client, template, "Article", cancellationToken, parent.Summary.Id);

        await FillZoneAsync(client, parent.Summary.Id, "body", "Live section", cancellationToken);
        await client.PostAsJsonAsync(
            $"{Pages}/{parent.Summary.Id}/publish",
            new PublishPageRequest(),
            cancellationToken);

        var deleted = await client.DeleteAsync($"{Pages}/{parent.Summary.Id}", cancellationToken);

        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        var subtree = (await deleted.Content.ReadFromJsonAsync<SubtreeResult>(cancellationToken))!;

        // One delete of a section reads as one operation over two pages, which is the number a
        // confirmation dialog has to show before anybody agrees to it.
        subtree.AffectedPageIds.Should().BeEquivalentTo([parent.Summary.Id, child.Summary.Id]);
        subtree.UnpublishedCount.Should().Be(1);

        var gone = await client.GetAsync($"{Pages}/{child.Summary.Id}", cancellationToken);
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var bin = await client.GetFromJsonAsync<List<RecycleBinEntry>>(
            $"{Pages}/recycle-bin",
            cancellationToken);

        bin.Should().ContainSingle(entry => entry.Id == parent.Summary.Id && entry.IsSubtreeRoot);

        var restored = await client.PostAsync($"{Pages}/{parent.Summary.Id}/restore", null, cancellationToken);

        restored.StatusCode.Should().Be(HttpStatusCode.OK);

        var back = await client.GetFromJsonAsync<PageDetail>($"{Pages}/{parent.Summary.Id}", cancellationToken);

        // Restored as a draft, never live: nothing reappears publicly that nobody has looked at
        // since it was deleted (spec section 14.10).
        back!.Summary.PublishedVersionNumber.Should().BeNull();
    }

    [Fact]
    public async Task APermanentDeleteNeedsUserManagementAndAnEmptiedSubtree()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await AdministratorAsync(_factory, cancellationToken);
        var template = await CreateTemplateAsync(client, "lifecycle-purge", cancellationToken);
        var page = await CreatePageAsync(client, template, "Disposable", cancellationToken);

        using var editor = await ClientAsync(_factory, cancellationToken, CmsRoles.Editor);

        var beforeDelete = await client.DeleteAsync($"{Pages}/{page.Summary.Id}/permanent", cancellationToken);

        await client.DeleteAsync($"{Pages}/{page.Summary.Id}", cancellationToken);

        var asEditor = await editor.DeleteAsync($"{Pages}/{page.Summary.Id}/permanent", cancellationToken);
        var asAdministrator = await client.DeleteAsync($"{Pages}/{page.Summary.Id}/permanent", cancellationToken);

        // Refused while the page is still live: the recycle bin is the step that makes this
        // deliberate rather than a one-request mistake.
        beforeDelete.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodesAsync(beforeDelete, cancellationToken)).Should().Contain(PageCodes.PageNotDeleted);

        // An Editor can empty a page into the bin and cannot destroy its history — the one
        // irreversible operation in the system sits with user management (task P2-17).
        asEditor.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        asAdministrator.StatusCode.Should().Be(HttpStatusCode.OK);
        (await asAdministrator.Content.ReadFromJsonAsync<PurgeResult>(cancellationToken))!
            .VersionsRemoved.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnEditorMayListTheRecycleBinAndAViewerMayNot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var editor = await ClientAsync(_factory, cancellationToken, CmsRoles.Editor);
        using var viewer = await ClientAsync(_factory, cancellationToken, CmsRoles.Viewer);

        var allowed = await editor.GetAsync($"{Pages}/recycle-bin", cancellationToken);
        var refused = await viewer.GetAsync($"{Pages}/recycle-bin", cancellationToken);

        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        // The bin lists what has been deleted, which is a deletion concern rather than a read one.
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
