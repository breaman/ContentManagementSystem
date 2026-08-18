using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Routing;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Routing;

/// <summary>
/// Redirect creation, chain flattening, loop refusal, hit counting, and CSV round trips
/// (tasks P3-05, P3-06, and P3-22, spec section 10.5).
/// </summary>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class RedirectServiceTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task ARedirectSendsItsSourceToItsDestination()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        var created = await redirects.CreateAsync(
            new CreateRedirectRequest("/old-news", ToUrl: "/news"),
            cancellationToken);

        created.IsSuccess.Should().BeTrue(Because(created));
        created.Value!.StatusCode.Should().Be(301, "a moved URL is permanently moved unless told otherwise");

        var match = await redirects.ResolveAsync("/old-news", cancellationToken);

        match.Should().NotBeNull();
        match.TargetUrl.Should().Be("/news");
        match.StatusCode.Should().Be(301);
    }

    [Test]
    public async Task ASourceIsNormalizedSoOneRuleCoversEverySpellingOfTheUrl()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        await redirects.CreateAsync(new CreateRedirectRequest("/Old-News/", ToUrl: "/news"), cancellationToken);

        foreach (var spelling in new[] { "/old-news", "/Old-News", "/old-news/", "old-news" })
        {
            (await redirects.ResolveAsync(spelling, cancellationToken))
                .Should().NotBeNull($"'{spelling}' is the same address");
        }
    }

    [Test]
    public async Task AChainIsFlattenedOnWriteRatherThanWalkedOnEveryRequest()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        await redirects.CreateAsync(new CreateRedirectRequest("/a", ToUrl: "/b"), cancellationToken);
        await redirects.CreateAsync(new CreateRedirectRequest("/b", ToUrl: "/c"), cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        // Acceptance criterion P3 #5. The visitor pays one round trip, and the chain cannot creep
        // longer as a site is reorganised over years.
        var stored = await _bench.Context.Redirects
            .AsNoTracking()
            .SingleAsync(redirect => redirect.FromUrl == "/a", cancellationToken);

        stored.ToUrl.Should().Be("/c", "A was rewritten when B → C was created");

        (await redirects.ResolveAsync("/a", cancellationToken))!.TargetUrl.Should().Be("/c");
    }

    [Test]
    public async Task ARedirectToItselfIsRefusedAtWriteTime()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var refused = await _bench.Resolve<IRedirectService>().CreateAsync(
            new CreateRedirectRequest("/loop", ToUrl: "/loop"),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(RoutingCodes.Loop);
    }

    [Test]
    public async Task AChainThatWouldCloseIsRefusedAtWriteTime()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        await redirects.CreateAsync(new CreateRedirectRequest("/a", ToUrl: "/b"), cancellationToken);
        await redirects.CreateAsync(new CreateRedirectRequest("/b", ToUrl: "/c"), cancellationToken);

        // Flattening leaves A → C and B → C stored, so this is the case the trivial self-reference
        // check misses: C → A closes a cycle through two rows neither of which mentions C.
        var refused = await redirects.CreateAsync(
            new CreateRedirectRequest("/c", ToUrl: "/a"),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(RoutingCodes.Loop);
    }

    [Test]
    public async Task AManualRedirectSurvivesATreeMoveThatWouldOtherwiseOverwriteIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Careers", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        var redirects = _bench.Resolve<IRedirectService>();

        // Somebody decides /jobs belongs to an external site.
        await redirects.CreateAsync(
            new CreateRedirectRequest("/jobs", ToUrl: "/external-careers"),
            cancellationToken);

        // A tree move now wants to leave an automatic redirect at the same source.
        var overwritten = await redirects.RecordAutomaticAsync("/jobs", page.Summary.Id, cancellationToken);

        overwritten.Should().BeFalse("a person's decision about a URL outranks a tree move");

        _bench.Context.ChangeTracker.Clear();

        (await redirects.ResolveAsync("/jobs", cancellationToken))!.TargetUrl.Should().Be("/external-careers");
    }

    [Test]
    public async Task ADisabledRedirectIsNotServedButStillHoldsItsSourceUrl()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        var created = await redirects.CreateAsync(
            new CreateRedirectRequest("/retired", ToUrl: "/current"),
            cancellationToken);

        await redirects.UpdateAsync(
            created.Value!.Id,
            new UpdateRedirectRequest(IsEnabled: false),
            cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        (await redirects.ResolveAsync("/retired", cancellationToken)).Should().BeNull();

        // The row is still there, so re-enabling it is not a unique-index violation waiting to
        // happen — which is why that index is unfiltered, unlike the one on routes.
        (await _bench.Context.Redirects.CountAsync(cancellationToken)).Should().Be(1);
    }

    [Test]
    public async Task FollowingARedirectCountsAHit()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        var created = await redirects.CreateAsync(
            new CreateRedirectRequest("/counted", ToUrl: "/target"),
            cancellationToken);

        await redirects.RecordHitAsync(created.Value!.Id, cancellationToken);
        await redirects.RecordHitAsync(created.Value.Id, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Redirects
            .AsNoTracking()
            .SingleAsync(cancellationToken);

        // The count is what identifies a dead redirect worth pruning, which is the entire reason the
        // column exists (spec section 10.5).
        stored.HitCount.Should().Be(2);
        stored.LastHitOn.Should().NotBeNull();
    }

    [Test]
    public async Task ADestinationExpressedAsAPageIsReportedAsThatPagesCurrentUrl()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken, PageWorkbench.TextZone("hero"));
        var page = await _bench.AddPageAsync(template, "Support", cancellationToken);

        (await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken)).IsSuccess.Should().BeTrue();

        var redirects = _bench.Resolve<IRedirectService>();

        var created = await redirects.CreateAsync(
            new CreateRedirectRequest("/helpdesk", ToPageId: page.Summary.Id),
            cancellationToken);

        created.IsSuccess.Should().BeTrue(Because(created));

        // A client holding the row alone cannot work this out, which is why the API resolves it.
        created.Value!.ToUrl.Should().BeNull();
        created.Value.ResolvedToUrl.Should().Be("/support");
    }

    [Test]
    [Arguments(null, "/somewhere", "redirect.source-invalid")]
    [Arguments("/", "/somewhere", "redirect.source-invalid")]
    [Arguments("/from", null, "redirect.destination-invalid")]
    [Arguments("/from", "/to", "redirect.status-invalid")]
    public async Task AnUnusableRedirectIsRefusedWithTheCodeThatNamesTheRemedy(
        string? from,
        string? to,
        string expectedCode)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // The status case reuses the valid pair and supplies a status no browser treats as a
        // redirect, which is the only way to reach that code from this table.
        short status = expectedCode == RoutingCodes.StatusInvalid ? (short)307 : (short)301;

        var refused = await _bench.Resolve<IRedirectService>().CreateAsync(
            new CreateRedirectRequest(from, ToUrl: to, StatusCode: status),
            cancellationToken);

        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should().Contain(diagnostic => diagnostic.Code == expectedCode);
    }

    [Test]
    public async Task NamingBothAPageAndAUrlIsRefusedRatherThanResolvedByPrecedence()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var template = await _bench.AddTemplateAsync("landing", cancellationToken);
        var page = await _bench.AddPageAsync(template, "Somewhere", cancellationToken);

        var refused = await _bench.Resolve<IRedirectService>().CreateAsync(
            new CreateRedirectRequest("/from", ToPageId: page.Summary.Id, ToUrl: "/elsewhere"),
            cancellationToken);

        // A request carrying two destinations was built by something that does not know which one it
        // means, and picking one for it hides the bug.
        refused.Outcome.Should().Be(CmsOutcome.Invalid);
        refused.Diagnostics.Diagnostics.Should()
            .Contain(diagnostic => diagnostic.Code == RoutingCodes.DestinationInvalid);
    }

    [Test]
    public async Task ACsvImportCreatesRedirectsAndWarnsAboutTheRowsItSkipped()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        const string csv = """
            from,to,status,notes
            /legacy/one,/one,301,migrated
            /legacy/two,/two,302,
            /legacy/three,/three,418,teapot
            /legacy/four,
            /legacy/one,/duplicate,301,
            /legacy/five,/five
            """;

        var result = await _bench.Resolve<IRedirectService>().ImportAsync(csv, cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));
        result.Value!.Created.Should().Be(3, "one, two, and five are usable");
        result.Value.Skipped.Should().Be(3, "an unusable status, a missing destination, and a repeat");
        result.Value.Updated.Should().Be(0, "none of these sources had a redirect already");

        // A legacy list is thousands of rows long and always has a few bad ones. Refusing the file
        // would leave the operator editing a spreadsheet with no report of what was wrong.
        result.Diagnostics.Diagnostics.Should().HaveCount(3);
        result.Diagnostics.Diagnostics.Should()
            .OnlyContain(diagnostic => diagnostic.Code == RoutingCodes.ImportRowInvalid);
        result.Diagnostics.HasErrors.Should().BeFalse("a skipped row is a warning, not a failure");

        _bench.Context.ChangeTracker.Clear();

        var redirects = _bench.Resolve<IRedirectService>();
        (await redirects.ResolveAsync("/legacy/two", cancellationToken))!.StatusCode.Should().Be(302);
        (await redirects.ResolveAsync("/legacy/five", cancellationToken))!.TargetUrl.Should().Be("/five");
        (await redirects.ResolveAsync("/legacy/three", cancellationToken)).Should().BeNull();
    }

    [Test]
    public async Task AnExportCanBeImportedBackUnchanged()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var redirects = _bench.Resolve<IRedirectService>();

        await redirects.CreateAsync(
            new CreateRedirectRequest("/one", ToUrl: "/first", Notes: "with, a comma"),
            cancellationToken);
        await redirects.CreateAsync(
            new CreateRedirectRequest("/two", ToUrl: "/second", StatusCode: 302),
            cancellationToken);

        var exported = await redirects.ExportAsync(cancellationToken);
        exported.IsSuccess.Should().BeTrue(Because(exported));

        // Round-trippable on purpose: the realistic way a large list gets cleaned up is export, edit
        // in a spreadsheet, re-import.
        var reimported = await redirects.ImportAsync(exported.Value!, cancellationToken);

        reimported.IsSuccess.Should().BeTrue(Because(reimported));
        reimported.Value!.Skipped.Should().Be(0, "an export its own importer cannot read is useless");
        reimported.Value.Created.Should().Be(0, "every row already exists");
        reimported.Value.Updated.Should().Be(2, "each row restated what was already stored");

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Redirects
            .AsNoTracking()
            .OrderBy(redirect => redirect.FromUrl)
            .ToListAsync(cancellationToken);

        stored.Should().HaveCount(2);
        stored[0].Notes.Should().Be("with, a comma", "a quoted cell survives the round trip");
        stored[1].StatusCode.Should().Be(302);
    }

    [Test]
    public async Task ImportIsCaseAndSlashInsensitiveAboutSourcesItHasAlreadySeen()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        const string csv = """
            /Legacy/Page/,/target
            /legacy/page,/other
            """;

        var result = await _bench.Resolve<IRedirectService>().ImportAsync(csv, cancellationToken);

        // Both lines name one address. Without the check they would reach the save as two inserts
        // and fail on the unique index, taking the whole file with them.
        result.Value!.Created.Should().Be(1);
        result.Value.Skipped.Should().Be(1);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.Redirects.AsNoTracking().SingleAsync(cancellationToken);
        stored.FromUrl.Should().Be("/legacy/page");
        stored.FromUrlHash.Should().Equal(SiteUrls.Hash("/legacy/page"));
    }

    /// <summary>Renders a refusal's diagnostics into an assertion message.</summary>
    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
}
