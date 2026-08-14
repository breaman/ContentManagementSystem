using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Server.Tests.Content;

/// <summary>
/// Advisory edit locks (task P2-15, spec section 11.8, ADR 0012).
/// </summary>
/// <remarks>
/// The rule every case here is really about: <strong>a lock never prevents editing</strong>. Locks
/// that block are locks that get stuck, and the authoritative defence against a lost update is the
/// row version on the draft — which works whether or not anybody acquired anything.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class EditLockTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task ASecondEditorSeesWhoHoldsTheLockAndCanStillEdit()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await PageAsync(cancellationToken);
        var locks = _bench.Resolve<IEditLockService>();

        var elena = await AddUserAsync("elena", cancellationToken);
        var marcus = await AddUserAsync("marcus", cancellationToken);

        _bench.Users.UserId = elena;
        var acquired = await locks.AcquireAsync(page, cancellationToken: cancellationToken);

        acquired.IsSuccess.Should().BeTrue();
        acquired.Value!.IsMine.Should().BeTrue();
        acquired.Value.UserName.Should().Be("elena");

        _bench.Users.UserId = marcus;
        var seen = await locks.AcquireAsync(page, cancellationToken: cancellationToken);

        // Success carrying somebody else's lock, not a refusal. The caller decides whether to warn;
        // nothing here stops the second editor from typing.
        seen.IsSuccess.Should().BeTrue();
        seen.Value!.IsMine.Should().BeFalse();
        seen.Value.UserId.Should().Be(elena);
        seen.Value.UserName.Should().Be("elena");

        // And the write itself goes through, which is the property the whole design turns on.
        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page,
            new SaveDraftRequest(await PayloadAsync(page, "Marcus typed anyway", cancellationToken), null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ALockCanBeTakenOverExplicitly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await PageAsync(cancellationToken);
        var locks = _bench.Resolve<IEditLockService>();

        var elena = await AddUserAsync("elena", cancellationToken);
        var marcus = await AddUserAsync("marcus", cancellationToken);

        _bench.Users.UserId = elena;
        await locks.AcquireAsync(page, cancellationToken: cancellationToken);

        _bench.Users.UserId = marcus;
        var taken = await locks.AcquireAsync(page, takeOver: true, cancellationToken);

        taken.Value!.IsMine.Should().BeTrue();
        taken.Value.UserId.Should().Be(marcus);

        _bench.Context.ChangeTracker.Clear();

        // Still one row: the table's primary key is the page, which is what makes "at most one lock
        // per page" a fact the schema enforces rather than a rule this service remembers.
        var rows = await _bench.Context.EditLocks.AsNoTracking().ToListAsync(cancellationToken);

        rows.Should().ContainSingle().Which.UserId.Should().Be(marcus);
    }

    [Fact]
    public async Task ALockExpiresAfterTwoMinutesOfSilenceAndAHeartbeatKeepsItAlive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await PageAsync(cancellationToken);
        var locks = _bench.Resolve<IEditLockService>();

        var elena = await AddUserAsync("elena", cancellationToken);
        var marcus = await AddUserAsync("marcus", cancellationToken);

        _bench.Users.UserId = elena;
        var opened = await locks.AcquireAsync(page, cancellationToken: cancellationToken);
        var openedAt = opened.Value!.AcquiredOn;

        // A heartbeat inside the window keeps the lock and leaves AcquiredOn where it was, so
        // "opened at 09:00" keeps meaning what it says over a long editing session.
        _bench.Clock.Advance(IEditLockService.HeartbeatInterval);
        var beat = await locks.AcquireAsync(page, cancellationToken: cancellationToken);

        beat.Value!.AcquiredOn.Should().Be(openedAt);
        beat.Value.HeartbeatOn.Should().BeAfter(openedAt);

        _bench.Users.UserId = marcus;
        (await locks.GetAsync(page, cancellationToken)).Value.Should().NotBeNull();

        _bench.Clock.Advance(IEditLockService.Expiry);

        // Expiry is enforced on read, so a stale row can never be shown as a live one just because
        // nothing has swept the table recently.
        (await locks.GetAsync(page, cancellationToken)).Value
            .Should().BeNull("the holder has gone quiet for two minutes");

        var inherited = await locks.AcquireAsync(page, cancellationToken: cancellationToken);
        inherited.Value!.IsMine.Should().BeTrue("an expired lock is taken without asking");
    }

    [Fact]
    public async Task TheReaperClearsLocksNobodyIsHoldingAnyMore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await PageAsync(cancellationToken);
        var locks = _bench.Resolve<IEditLockService>();

        _bench.Users.UserId = await AddUserAsync("elena", cancellationToken);
        await locks.AcquireAsync(page, cancellationToken: cancellationToken);

        (await locks.ReapAsync(cancellationToken)).Should().Be(0, "the lock is live");

        _bench.Clock.Advance(IEditLockService.Expiry);

        (await locks.ReapAsync(cancellationToken)).Should().Be(1);

        _bench.Context.ChangeTracker.Clear();
        (await _bench.Context.EditLocks.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task ReleasingSomebodyElsesLockDoesNothingAndIsNotAnError()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await PageAsync(cancellationToken);
        var locks = _bench.Resolve<IEditLockService>();

        var elena = await AddUserAsync("elena", cancellationToken);
        var marcus = await AddUserAsync("marcus", cancellationToken);

        _bench.Users.UserId = elena;
        await locks.AcquireAsync(page, cancellationToken: cancellationToken);

        // The ordinary way to reach this is an editor closing a tab they had already been taken over
        // from; an alarming message would be in front of the wrong person.
        _bench.Users.UserId = marcus;
        var released = await locks.ReleaseAsync(page, cancellationToken);

        released.IsSuccess.Should().BeTrue();
        released.Value.Should().BeFalse();

        _bench.Users.UserId = elena;
        (await locks.ReleaseAsync(page, cancellationToken)).Value.Should().BeTrue();

        _bench.Context.ChangeTracker.Clear();
        (await _bench.Context.EditLocks.CountAsync(cancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task ALockOnAPageThatIsNotThereIsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        (await _bench.Resolve<IEditLockService>().AcquireAsync(999_999, cancellationToken: cancellationToken))
            .Outcome.Should().Be(CmsOutcome.NotFound);
    }

    [Fact]
    public async Task AViewerMaySeeALockAndMayNotTakeOne()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var page = await PageAsync(cancellationToken);

        await using var viewer = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead),
            cancellationToken);

        var locks = viewer.Resolve<IEditLockService>();

        // Reading who is editing is part of seeing the page; taking the lock is part of editing it.
        // Both checks run in the service, so they hold for a caller that never went through an
        // endpoint at all.
        (await locks.GetAsync(page, cancellationToken)).IsSuccess.Should().BeTrue();
        (await locks.AcquireAsync(page, cancellationToken: cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);
    }

    private async Task<int> PageAsync(CancellationToken cancellationToken)
    {
        var template = await _bench.AddTemplateAsync(
            "landing",
            cancellationToken,
            PageWorkbench.TextZone("hero"));

        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        return page.Summary.Id;
    }

    private async Task<string> PayloadAsync(int pageId, string text, CancellationToken cancellationToken)
    {
        var draft = await _bench.DraftOfAsync(pageId, cancellationToken);
        var key = await _bench.Context.Templates
            .AsNoTracking()
            .Where(template => template.Id == draft.TemplateId)
            .Select(template => template.Key)
            .SingleAsync(cancellationToken);

        return $$"""
        { "schemaVersion": 1, "templateKey": "{{key}}", "templateRevision": 1,
          "zones": { "hero": { "type": "plainText", "value": "{{text}}" } } }
        """;
    }

    private async Task<int> AddUserAsync(string name, CancellationToken cancellationToken)
    {
        var user = new Data.Models.User
        {
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            MemberSince = DateTimeOffset.UtcNow,
        };

        _bench.Context.Users.Add(user);
        await _bench.Context.SaveChangesAsync(cancellationToken);

        return user.Id;
    }
}
