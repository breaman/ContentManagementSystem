using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Media.Library;
using ContentManagementSystem.Core.Preview;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Preview;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Workflow;

/// <summary>
/// Every content and media entry point, called with a guessed id across an access boundary
/// (task P7-07).
/// </summary>
/// <remarks>
/// The sweep exists because an authorization bug is almost never a missing check on the screen an
/// editor uses — it is a service somebody added later that reads an id out of a request and trusts
/// it. So this does not navigate anywhere: it takes the id of a page the caller is refused and hands
/// it to each service in turn.
/// <para>
/// Read refusals answer <em>not found</em> and everything else answers <em>forbidden</em>, and both
/// are asserted. A 403 where a 404 was expected is an existence oracle: an outsider can map the
/// content tree by watching which guesses come back which way.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class IdorSweepTests(SqlServerFixture fixture)
{
    /// <summary>Identity of the caller every rule in this suite is written about.</summary>
    private const int Caller = 1;

    [Test]
    public async Task EveryContentServiceRefusesAGuessedIdOutsideTheCallersSection()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var mine = await workbench.AddPageAsync(template, "Products", cancellationToken);
        var theirs = await workbench.AddPageAsync(template, "Finance", cancellationToken);

        // One allow rule turns the permission into an allowlist, so everything outside /products is
        // refused without a rule having to be written about it — which is the point of P7 #5 and the
        // reason a guessed id is the interesting case.
        foreach (var permission in new[]
        {
            CmsPermissions.ContentRead,
            CmsPermissions.ContentEdit,
            CmsPermissions.ContentPublish,
            CmsPermissions.ContentDelete,
        })
        {
            workbench.Context.PageAcls.Add(new PageAcl
            {
                PageId = mine.Summary.Id,
                PrincipalType = AclPrincipalType.User,
                PrincipalId = Caller,
                Permission = permission,
                IsAllow = true,
                IsInherited = true,
            });
        }

        await workbench.Context.SaveChangesAsync(cancellationToken);

        var guessed = theirs.Summary.Id;

        await using var request = workbench.NewScope();
        var services = request.ServiceProvider;

        var pages = services.GetRequiredService<IPageService>();
        var drafts = services.GetRequiredService<IDraftService>();
        var versions = services.GetRequiredService<IVersionService>();
        var diffs = services.GetRequiredService<IContentDiffService>();
        var publishing = services.GetRequiredService<IPublishingService>();
        var locks = services.GetRequiredService<IEditLockService>();
        var duplication = services.GetRequiredService<IDuplicationService>();
        var bin = services.GetRequiredService<IRecycleBinService>();
        var tokens = services.GetRequiredService<IPreviewTokenService>();

        // Reads: absent, not refused.
        (await pages.GetAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);
        (await drafts.GetAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);
        (await versions.ListAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);
        (await diffs.CompareAsync(guessed, 1, 2, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);
        (await publishing.ValidateAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);
        (await tokens.ListAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);
        (await duplication.DuplicateAsync(guessed, cancellationToken: cancellationToken))
            .Outcome.Should().Be(CmsOutcome.NotFound);

        // Writes: refused outright, because by now the caller has been told the page is there.
        (await pages.PatchMetadataAsync(
            guessed,
            new PatchPageMetadataRequest { Title = "Mine now" },
            cancellationToken: cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);

        (await pages.MoveAsync(guessed, new MovePageRequest(mine.Summary.Id), cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);

        (await drafts.SaveAsync(
            guessed,
            new SaveDraftRequest("{}", null),
            cancellationToken: cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);

        (await drafts.DiscardAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await drafts.CheckpointAsync(guessed, "Mine", cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await versions.RestoreAsync(guessed, 1, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);

        (await publishing.PublishAsync(guessed, true, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);

        (await publishing.UnpublishAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await locks.AcquireAsync(guessed, cancellationToken: cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);

        (await locks.ReleaseAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await bin.DeleteAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await bin.RestoreAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await bin.DescribeAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.NotFound);

        (await tokens.IssueAsync(
            new CreatePreviewTokenRequest(guessed, null),
            cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);

        (await tokens.RevokeAllAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);

        // And the page is untouched by any of it.
        var stored = await workbench.DraftOfAsync(guessed, cancellationToken);
        stored.Title.Should().Be("Finance");
    }

    [Test]
    public async Task EveryMediaServiceRefusesACallerHoldingNoMediaPermission()
    {
        // Media has no tree and therefore no section ACLs: the library is one flat space and section
        // 21.2 is about pages. What is swept here is the other half of the same question — whether
        // an id handed straight to a media service is checked at all.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(
            fixture,
            new StubAuthorization(CmsPermissions.ContentRead),
            cancellationToken);

        var media = workbench.Resolve<IMediaLibraryService>();
        const int guessed = 1;

        (await media.PatchAsync(guessed, new PatchMediaRequest { AltText = "Mine" }, cancellationToken))
            .Outcome.Should().Be(CmsOutcome.Forbidden);

        (await media.RevertEditsAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await media.DeleteAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await media.RestoreAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
        (await media.PurgeAsync(guessed, cancellationToken)).Outcome.Should().Be(CmsOutcome.Forbidden);
    }
}
