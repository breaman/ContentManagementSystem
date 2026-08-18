using System.Net;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Preview;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Delivery.Preview;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// Preview, end to end over HTTP (tasks P3-16 to P3-21 and P3-26, spec section 12).
/// </summary>
/// <remarks>
/// Content is arranged through the real page, draft, and publishing services and then requested with
/// an ordinary <c>HttpClient</c> — carrying an editor's roles for the authenticated path and no
/// identity at all for the shared-link one. Everything between is the application's own.
/// <para>
/// The template is <c>article</c>, a key a deployed component declares, so what the frame renders is
/// the reference template rather than the fallback layout.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PreviewTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    /// <summary>The reference template whose <c>cta</c> zone holds a <c>link</c> (task P3-10).</summary>
    private const string LinkTemplateKey = "marketing-landing";

    private PageWorkbench _bench = null!;

    /// <summary>
    /// The article template, given its zones once.
    /// </summary>
    /// <remarks>
    /// Cached because <c>UseTemplateAsync</c> <em>adds</em> zones rather than replacing them, so a
    /// second call for the same key collides on <c>(TemplateId, Key)</c> — which is correct
    /// behaviour and means a test creating two pages has to arrange the template once.
    /// </remarks>
    private Template? _template;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    // ---- Authenticated preview (task P3-16) ---------------------------------------------------

    [Test]
    public async Task AnUnpublishedPageRendersInPreviewForAnEditor()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Not for the public yet", cancellationToken);

        using var anonymous = _bench.CreateClient();
        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);

        using var publicResponse = await anonymous.GetAsync("/unreleased", cancellationToken);
        using var frame = await editor.GetAsync(
            $"/preview/{page.Summary.Id}/content", cancellationToken);

        // The other half of acceptance criterion P3 #2: the same page, at the same moment, is a 404
        // to the public and readable to an editor.
        publicResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
        frame.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await frame.Content.ReadAsStringAsync(cancellationToken);

        html.Should().Contain("Not for the public yet").And.Contain("data-template=\"article\"");
    }

    [Test]
    public async Task ThePreviewChromeCarriesTheVersionLabelStatusAndExit()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Work in progress", cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);
        using var response = await editor.GetAsync($"/preview/{page.Summary.Id}", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // The three things spec section 12.1 asks the toolbar for. Asserted literally, because the
        // mistake preview exists to prevent is somebody publishing having read the wrong version.
        html.Should().Contain("Unreleased")
            .And.Contain("v1")
            .And.Contain("Draft")
            .And.Contain("Exit preview")
            .And.Contain($"/admin/pages/{page.Summary.Id}");

        // The chrome frames the page; it does not contain it. That split is what makes the framed
        // document byte-identical to what the public site would serve.
        html.Should().Contain($"src=\"/preview/{page.Summary.Id}/content\"")
            .And.NotContain("Work in progress");
    }

    [Test]
    public async Task EveryPreviewResponseIsUnindexableAndUncacheable()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Secret", cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);

        foreach (var path in (string[])[$"/preview/{page.Summary.Id}", $"/preview/{page.Summary.Id}/content"])
        {
            using var response = await editor.GetAsync(path, cancellationToken);

            // The failure that matters is not one preview being cached — it is an unpublished page
            // sitting in a shared cache or a search index, where nothing the CMS does can evict it.
            response.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
            response.Headers.CacheControl!.NoStore.Should().BeTrue($"{path} must never be stored");
        }
    }

    [Test]
    public async Task PreviewShowsAnySpecificVersionAndNotOnlyTheDraft()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Offers", "The published words", cancellationToken);

        await PublishAsync(page.Summary.Id, cancellationToken);

        var publishedVersionId = await _bench.Context.Pages
            .AsNoTracking()
            .Where(candidate => candidate.Id == page.Summary.Id)
            .Select(candidate => candidate.PublishedVersionId!.Value)
            .SingleAsync(cancellationToken);

        await SaveDraftAsync(page.Summary.Id, TemplateKey, "The draft words", cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);

        var draft = await editor.GetStringAsync(
            $"/preview/{page.Summary.Id}/content", cancellationToken);
        var published = await editor.GetStringAsync(
            $"/preview/{page.Summary.Id}/content?version={publishedVersionId}", cancellationToken);

        // "Renders any version" is the whole of spec section 12.1, and the two must not be the same
        // document — a preview that quietly always showed the draft would pass a weaker assertion.
        draft.Should().Contain("The draft words").And.NotContain("The published words");
        published.Should().Contain("The published words").And.NotContain("The draft words");
    }

    [Test]
    public async Task AVersionBelongingToAnotherPageIsNotServedUnderThisOne()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var mine = await DraftPageAsync("Mine", "My words", cancellationToken);
        var theirs = await DraftPageAsync("Theirs", "Their words", cancellationToken);

        var theirVersionId = await _bench.Context.Pages
            .AsNoTracking()
            .Where(candidate => candidate.Id == theirs.Summary.Id)
            .Select(candidate => candidate.DraftVersionId!.Value)
            .SingleAsync(cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);
        using var response = await editor.GetAsync(
            $"/preview/{mine.Summary.Id}/content?version={theirVersionId}", cancellationToken);

        // The page and the version together are the address. Serving one page's content under
        // another's URL and metadata is how a preview link leaks a page nobody meant to share.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().NotContain("Their words");
    }

    [Test]
    public async Task AnAnonymousRequestForAnEditorsPreviewIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Not for the public yet", cancellationToken);

        using var anonymous = _bench.CreateClient(followRedirects: false);
        using var response = await anonymous.GetAsync(
            $"/preview/{page.Summary.Id}/content", cancellationToken);

        // Whatever the scheme does with an unauthenticated caller — challenge or refuse — what must
        // not happen is the content coming back.
        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().NotContain("Not for the public yet");
    }

    [Test]
    public async Task AReaderMayPreviewAndOnlyRolesHoldingContentReadMay()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Not for the public yet", cancellationToken);

        using var viewer = _bench.CreateClient(roles: CmsRoles.Viewer);
        using var mediaManager = _bench.CreateClient(roles: CmsRoles.MediaManager);

        using var allowed = await viewer.GetAsync($"/preview/{page.Summary.Id}/content", cancellationToken);
        using var refused = await mediaManager.GetAsync(
            $"/preview/{page.Summary.Id}/content", cancellationToken);

        // Content.Read is the permission that already means "may see unpublished content"; the
        // media manager holds none of the content permissions and must not reach a draft.
        allowed.StatusCode.Should().Be(HttpStatusCode.OK);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- Device widths (task P3-21) ------------------------------------------------------------

    [Test]
    public async Task TheFrameIsConstrainedToTheRequestedDeviceWidth()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Work in progress", cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);

        var desktop = await editor.GetStringAsync($"/preview/{page.Summary.Id}", cancellationToken);
        var mobile = await editor.GetStringAsync(
            $"/preview/{page.Summary.Id}?device=mobile", cancellationToken);

        desktop.Should().Contain("cms-preview-viewport--desktop");
        mobile.Should().Contain("cms-preview-viewport--mobile");

        // The device never reaches the framed page. If it did, a page could render differently
        // inside preview, which is the one thing preview must not permit.
        mobile.Should().Contain($"src=\"/preview/{page.Summary.Id}/content\"");
    }

    [Test]
    public async Task AnUnreadableDeviceFallsBackRatherThanFailing()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "Work in progress", cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);
        using var response = await editor.GetAsync(
            $"/preview/{page.Summary.Id}?device=watch", cancellationToken);

        // The parameter is a view preference in a URL people paste to each other; a mangled one must
        // produce a preview at the default width, not an error page.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("cms-preview-viewport--desktop");
    }

    // ---- Draft links inside preview (task P3-20) -----------------------------------------------

    [Test]
    public async Task AnInternalLinkToAnUnpublishedPageResolvesToItsDraftInsidePreviewAndIsBadged()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // marketing-landing rather than article, because its `cta` zone is the one the reference set
        // places a `link` in — a zone the deployed component does not render would make every
        // assertion below pass or fail for the wrong reason.
        var template = await _bench.UseTemplateAsync(
            LinkTemplateKey,
            cancellationToken,
            new Zone { Key = "cta", Name = "Call to action", FieldTypeKey = FieldTypeKeys.Link });

        var target = await _bench.AddPageAsync(template, "Unreleased section", cancellationToken);
        var host = await _bench.AddPageAsync(template, "Campaign", cancellationToken);

        await SaveLinkingDraftAsync(host.Summary.Id, target.Summary.Id, cancellationToken);
        await PublishAsync(host.Summary.Id, cancellationToken);

        using var editor = _bench.CreateClient(roles: CmsRoles.Editor);
        using var anonymous = _bench.CreateClient();

        var preview = await editor.GetStringAsync(
            $"/preview/{host.Summary.Id}/content", cancellationToken);
        var live = await anonymous.GetStringAsync("/campaign", cancellationToken);

        // Spec section 12.3: a reviewer can walk an unreleased section, and knows they are doing it.
        preview.Should().Contain("href=\"/unreleased-section\"")
            .And.Contain("cms-draft-badge")
            .And.Contain("Read on");

        // And the same link on the public page resolves to nothing at all, so no draft URL can leak
        // into a page that is otherwise entirely live.
        live.Should().Contain("Read on")
            .And.NotContain("/unreleased-section")
            .And.NotContain("cms-draft-badge");
    }

    // ---- Shared links (tasks P3-17, P3-18, and P3-26) -----------------------------------------

    [Test]
    public async Task AShareableLinkRendersForAnAnonymousBrowser()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client to review", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken);

        using var anonymous = _bench.CreateClient();

        using var chrome = await anonymous.GetAsync(issued.Url, cancellationToken);
        using var frame = await anonymous.GetAsync($"{issued.Url}/content", cancellationToken);

        // Half of acceptance criterion P3 #10. The client carries no cookie, no role header, and no
        // identity of any kind — the token is the whole of their authority.
        chrome.StatusCode.Should().Be(HttpStatusCode.OK);
        frame.StatusCode.Should().Be(HttpStatusCode.OK);

        (await frame.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("For the client to review");

        // No exit link: the holder has no backoffice to be returned to, and offering one would be an
        // invitation to a login screen they cannot pass.
        (await chrome.Content.ReadAsStringAsync(cancellationToken))
            .Should().NotContain("Exit preview").And.Contain("Link expires");
    }

    [Test]
    public async Task AShareableLinkIsNeverIndexableAndNeverCacheable()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken);

        using var anonymous = _bench.CreateClient();

        foreach (var path in (string[])[issued.Url, $"{issued.Url}/content"])
        {
            using var response = await anonymous.GetAsync(path, cancellationToken);

            response.Headers.GetValues("X-Robots-Tag").Should().Contain("noindex, nofollow");
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
        }
    }

    [Test]
    public async Task AShareableLinkServesExactlyTheVersionItWasIssuedForAndNotThePagesLatest()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Offers", "What the sender saw", cancellationToken);

        await PublishAsync(page.Summary.Id, cancellationToken);

        var sharedVersionId = await _bench.Context.Pages
            .AsNoTracking()
            .Where(candidate => candidate.Id == page.Summary.Id)
            .Select(candidate => candidate.PublishedVersionId!.Value)
            .SingleAsync(cancellationToken);

        var issued = await IssueAsync(page.Summary.Id, cancellationToken, versionId: sharedVersionId);

        // The page moves on: a new draft, and a publish that makes a different version live.
        await SaveDraftAsync(page.Summary.Id, TemplateKey, "Written after sharing", cancellationToken);
        await PublishAsync(page.Summary.Id, cancellationToken);

        using var anonymous = _bench.CreateClient();

        var html = await anonymous.GetStringAsync($"{issued.Url}/content", cancellationToken);

        // "Serves exactly one page version" (spec section 12.2). The token names a version rather
        // than a page, so two publishes later it is still showing the document the reviewer was
        // asked to comment on — which is what makes their comments answerable.
        html.Should().Contain("What the sender saw").And.NotContain("Written after sharing");

        issued.Summary.PageVersionId.Should().Be(sharedVersionId);
    }

    [Test]
    public async Task ALinkSharingTheDraftFollowsTheDraftRowBecauseThatRowIsTheMutableOne()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "First pass", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken);

        await SaveDraftAsync(page.Summary.Id, TemplateKey, "Second pass", cancellationToken);

        using var anonymous = _bench.CreateClient();

        var html = await anonymous.GetStringAsync($"{issued.Url}/content", cancellationToken);

        // Pinned to a version id, and the draft *is* one version whose content keeps changing
        // (spec section 11.1) — so a link shared against the draft shows the latest save rather than
        // the words that were there when it was sent. Asserted rather than left to be discovered:
        // it is the right behaviour for "look at what I am working on" and the wrong one for "sign
        // this off", and the difference is which version the sender picks.
        html.Should().Contain("Second pass").And.NotContain("First pass");
    }

    [Test]
    public async Task AnExpiredLinkStopsWorkingOnSchedule()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken, expiresInDays: 7);

        using var anonymous = _bench.CreateClient();

        (await anonymous.GetAsync($"{issued.Url}/content", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Six days on, still good; eight days on, gone. Both halves, because a link that expired
        // immediately would pass an assertion that only checked the second.
        _bench.Clock.Advance(TimeSpan.FromDays(6));

        (await anonymous.GetAsync($"{issued.Url}/content", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        _bench.Clock.Advance(TimeSpan.FromDays(2));

        using var expired = await anonymous.GetAsync($"{issued.Url}/content", cancellationToken);

        // 410 rather than 404: this URL worked and has stopped, which is a different thing to say to
        // an intermediary and to the person reading the page.
        expired.StatusCode.Should().Be(HttpStatusCode.Gone);
        (await expired.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("expired").And.NotContain("For the client");
    }

    [Test]
    public async Task AnExpiryBeyondThirtyDaysIsRefusedRatherThanClamped()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);

        var result = await _bench.Resolve<IPreviewTokenService>().IssueAsync(
            new CreatePreviewTokenRequest(page.Summary.Id, ExpiresInDays: 365),
            cancellationToken);

        // A link somebody believes lasts a year and which actually lasts thirty days is a support
        // ticket on day thirty-one, and this request is the last moment the mistake is visible.
        result.IsSuccess.Should().BeFalse();
        result.Diagnostics.Diagnostics.Should().Contain(
            diagnostic => diagnostic.Code == PreviewCodes.ExpiryInvalid);
    }

    [Test]
    public async Task ARevokedLinkStopsWorkingImmediately()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken);

        using var anonymous = _bench.CreateClient();

        (await anonymous.GetAsync($"{issued.Url}/content", cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var revoked = await _bench.Resolve<IPreviewTokenService>()
            .RevokeAsync(issued.Summary.Id, cancellationToken);

        revoked.IsSuccess.Should().BeTrue();
        _bench.Context.ChangeTracker.Clear();

        using var response = await anonymous.GetAsync($"{issued.Url}/content", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().NotContain("For the client");

        // The row survives the revocation. "This link was revoked on the 3rd, by this person" is the
        // answer somebody needs when a stakeholder reports that a link stopped working, and it is
        // the only record of who could once read an unpublished page.
        var stored = await _bench.Context.PreviewTokens
            .AsNoTracking()
            .SingleAsync(token => token.Id == issued.Summary.Id, cancellationToken);

        stored.RevokedOn.Should().NotBeNull();
    }

    [Test]
    public async Task RevocationInBulkTakesEveryLiveLinkForAPage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);

        var first = await IssueAsync(page.Summary.Id, cancellationToken);
        var second = await IssueAsync(page.Summary.Id, cancellationToken);

        var tokens = _bench.Resolve<IPreviewTokenService>();

        (await tokens.RevokeAsync(first.Summary.Id, cancellationToken)).IsSuccess.Should().BeTrue();
        _bench.Context.ChangeTracker.Clear();

        var revoked = await tokens.RevokeAllAsync(page.Summary.Id, cancellationToken);

        // One, not two: the already-revoked link is not revoked again, so the count is the number of
        // links this action actually took away rather than the number that exist.
        revoked.Value.Should().Be(1);
        _bench.Context.ChangeTracker.Clear();

        using var anonymous = _bench.CreateClient();

        foreach (var issued in (Shared.Contracts.Preview.IssuedPreviewToken[])[first, second])
        {
            (await anonymous.GetAsync($"{issued.Url}/content", cancellationToken))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task ALinkIsSpentByViewingTheContentAndNotByTheChromeAroundIt()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "One look only", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken, maxUses: 1);

        using var anonymous = _bench.CreateClient();

        // A single-use link that spent its one view on the toolbar would never show anybody a page.
        (await anonymous.GetAsync(issued.Url, cancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var first = await anonymous.GetAsync($"{issued.Url}/content", cancellationToken);
        using var second = await anonymous.GetAsync($"{issued.Url}/content", cancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Gone);

        (await second.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("used up").And.NotContain("One look only");
    }

    [Test]
    public async Task TheTokenIsNotRecoverableFromTheDatabase()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.PreviewTokens
            .AsNoTracking()
            .SingleAsync(token => token.Id == issued.Summary.Id, cancellationToken);

        // The rest of acceptance criterion P3 #10, asserted against the row rather than against the
        // API: whoever can read this table — a backup, a reporting account, a leaked audit export —
        // holds 32 bytes of SHA-256 output and no way to turn it back into a working link.
        stored.TokenHash.Should().HaveCount(32);
        System.Text.Encoding.UTF8.GetString(stored.TokenHash).Should().NotContain(issued.Token);
        Convert.ToBase64String(stored.TokenHash).Should().NotBe(issued.Token);

        // And the hash really is of the token, so the check above is not passing vacuously.
        PreviewTokens.TryHash(issued.Token, out var expected).Should().BeTrue();
        stored.TokenHash.Should().Equal(expected);
    }

    [Test]
    public async Task TheSharedPreviewIsRateLimitedAndTheRestOfTheSiteIsNot()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var anonymous = _bench.CreateClient();

        // Fired at a token that was never issued, so what is being measured is the limiter rather
        // than anything about a particular link — which is also the traffic the limit exists for
        // (spec section 12.2): somebody walking the token space.
        var token = PreviewTokens.Create().Token;
        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt <= PreviewEndpointRouteBuilderExtensions.SharedRequestsPerWindow; attempt++)
        {
            using var response = await anonymous.GetAsync($"/preview/s/{token}", cancellationToken);
            statuses.Add(response.StatusCode);
        }

        statuses.Should().Contain(HttpStatusCode.TooManyRequests,
            "the shared preview must stop answering once the window's budget is spent");

        // 429 rather than the default 503: a link being clicked too fast is the client's problem to
        // slow down about, and 503 tells every intermediary the site itself is unhealthy.
        statuses.Should().NotContain(HttpStatusCode.ServiceUnavailable);

        // And the limit is on this surface alone. A limiter in front of the whole site is a
        // denial-of-service tool pointed at its own visitors.
        using var delivery = await anonymous.GetAsync("/nothing/here", cancellationToken);

        delivery.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ATokenThatWasNeverIssuedIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var anonymous = _bench.CreateClient();

        foreach (var candidate in (string[])["not-a-token", PreviewTokens.Create().Token])
        {
            using var response = await anonymous.GetAsync(
                $"/preview/s/{candidate}/content", cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Test]
    public async Task ALinkToARecycledPageSaysSoRatherThanClaimingToBeInvalid()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await DraftPageAsync("Unreleased", "For the client", cancellationToken);
        var issued = await IssueAsync(page.Summary.Id, cancellationToken, maxUses: 2);

        (await _bench.Resolve<IRecycleBinService>().DeleteAsync(page.Summary.Id, cancellationToken))
            .IsSuccess.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();

        using var anonymous = _bench.CreateClient();
        using var response = await anonymous.GetAsync($"{issued.Url}/content", cancellationToken);

        // The two answers send the reviewer to different people: a deleted page means asking the
        // editor to restore it, an invalid link means asking for a new one.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(cancellationToken))
            .Should().Contain("no longer available");

        // And the use was not spent on a page that could not be shown, so the link still has both
        // of its views if the page comes back.
        _bench.Context.ChangeTracker.Clear();

        var stored = await _bench.Context.PreviewTokens
            .AsNoTracking()
            .SingleAsync(token => token.Id == issued.Summary.Id, cancellationToken);

        stored.UseCount.Should().Be(0);
    }

    // ---- Fixtures ------------------------------------------------------------------------------

    private async Task<PageDetail> DraftPageAsync(
        string title,
        string text,
        CancellationToken cancellationToken)
    {
        _template ??= await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var template = _template;
        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        await SaveDraftAsync(page.Summary.Id, template.Key, text, cancellationToken);

        return page;
    }

    private async Task<IssuedPreviewToken> IssueAsync(
        int pageId,
        CancellationToken cancellationToken,
        int? expiresInDays = null,
        int? maxUses = null,
        int? versionId = null)
    {
        var result = await _bench.Resolve<IPreviewTokenService>().IssueAsync(
            new CreatePreviewTokenRequest(pageId, versionId, expiresInDays, maxUses),
            cancellationToken);

        result.IsSuccess.Should().BeTrue(Because(result));
        _bench.Context.ChangeTracker.Clear();

        return result.Value!;
    }

    private async Task PublishAsync(int pageId, CancellationToken cancellationToken)
    {
        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(pageId, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();
    }

    private async Task SaveDraftAsync(
        int pageId,
        string templateKey,
        string text,
        CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var payload =
            $$"""
            { "schemaVersion": 1, "templateKey": "{{templateKey}}", "templateRevision": 1,
              "zones": { "kicker": { "type": "plainText", "value": "{{text}}" } } }
            """;

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(payload, null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>Saves a draft whose call-to-action links at another page by id (decision D6).</summary>
    private async Task SaveLinkingDraftAsync(
        int pageId,
        int targetPageId,
        CancellationToken cancellationToken)
    {
        _bench.Context.ChangeTracker.Clear();

        var payload =
            $$"""
            { "schemaVersion": 1, "templateKey": "{{LinkTemplateKey}}", "templateRevision": 1,
              "zones": {
                "cta": { "type": "link", "kind": "page", "pageId": {{targetPageId}}, "text": "Read on" }
              } }
            """;

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            pageId,
            new SaveDraftRequest(payload, null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();
    }

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
