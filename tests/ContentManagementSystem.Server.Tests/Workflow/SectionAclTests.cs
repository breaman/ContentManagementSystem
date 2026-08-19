using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Core;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using FluentAssertions;

namespace ContentManagementSystem.Server.Tests.Workflow;

/// <summary>
/// Section-level access rules, enforced in the service layer (tasks P7-21, P7-25, criteria P7 #5
/// and P7 #6).
/// </summary>
/// <remarks>
/// The precedence arithmetic is asserted without a database in <c>AclFilterTests</c>. What needs one
/// is the half this suite is about: which rows bear on which caller, whether every entry point
/// actually asks, and whether a subtree with read denied disappears from the tree rather than
/// appearing greyed out.
/// <para>
/// Every refusal here is checked on the <em>service</em>, not on an endpoint. An endpoint policy is
/// a fast rejection at the door; the check that still runs when a service is called from a CLI verb
/// or a second endpoint somebody forgot to decorate is this one — which is what task P7-06 requires
/// and what the IDOR sweep goes looking for.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class SectionAclTests(SqlServerFixture fixture)
{
    /// <summary>Identity of the caller these tests write rules about.</summary>
    private const int Editor = 1;

    [Test]
    public async Task AnEditorGivenOneSectionIsRefusedEveryOther()
    {
        // Criterion P7 #5, with a guessed id: the caller never navigates to /about, they simply
        // send its id to the service and are refused.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var products = await workbench.AddPageAsync(template, "Products", cancellationToken);
        var bikes = await workbench.AddPageAsync(template, "Bikes", cancellationToken, products.Summary.Id);
        var about = await workbench.AddPageAsync(template, "About", cancellationToken);

        await AllowAsync(workbench, products.Summary.Id, CmsPermissions.ContentEdit, cancellationToken);

        // A second scope, because the access-rule resolver caches for the length of a request and
        // the rules above were written inside the first one.
        await using var request = workbench.NewScope();
        var pages = request.ServiceProvider.GetRequiredService<IPageService>();

        var inside = await pages.PatchMetadataAsync(
            bikes.Summary.Id,
            new PatchPageMetadataRequest { Title = "Bicycles" },
            cancellationToken: cancellationToken);

        var outside = await pages.PatchMetadataAsync(
            about.Summary.Id,
            new PatchPageMetadataRequest { Title = "About us" },
            cancellationToken: cancellationToken);

        inside.IsSuccess.Should().BeTrue("the rule reaches every descendant of the page it hangs on");
        outside.Outcome.Should().Be(CmsOutcome.Forbidden);

        var stored = await workbench.DraftOfAsync(about.Summary.Id, cancellationToken);
        stored.Title.Should().Be("About", "a refused edit changes nothing");
    }

    [Test]
    public async Task DenyingReadHidesTheSubtreeFromTheContentTreeEntirely()
    {
        // Criterion P7 #6 and task P7-25. "Hidden" means absent, not greyed out — an editor who can
        // see a node exists learns the name of a page they are not allowed to read.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var products = await workbench.AddPageAsync(template, "Products", cancellationToken);
        var secret = await workbench.AddPageAsync(template, "Unannounced", cancellationToken);
        var below = await workbench.AddPageAsync(template, "Pricing", cancellationToken, secret.Summary.Id);

        await DenyAsync(workbench, secret.Summary.Id, CmsPermissions.ContentRead, cancellationToken);

        await using var request = workbench.NewScope();
        var pages = request.ServiceProvider.GetRequiredService<IPageService>();

        var tree = await pages.TreeAsync(null, depth: 5, cancellationToken);

        tree.IsSuccess.Should().BeTrue();

        var titles = Flatten(tree.Value!).Select(node => node.Page.Title).ToList();

        titles.Should().Contain("Products");
        titles.Should().NotContain("Unannounced");
        titles.Should().NotContain("Pricing", "the descendants of a hidden page are hidden with it");

        // And the same id sent straight to the service answers not-found rather than forbidden: a
        // 403 that a 404 would not have produced tells the caller the page is there.
        var direct = await pages.GetAsync(below.Summary.Id, cancellationToken);
        direct.Outcome.Should().Be(CmsOutcome.NotFound);
    }

    [Test]
    public async Task ADeeperRuleReopensABranchInsideADeny()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var products = await workbench.AddPageAsync(template, "Products", cancellationToken);
        var bikes = await workbench.AddPageAsync(template, "Bikes", cancellationToken, products.Summary.Id);

        await DenyAsync(workbench, products.Summary.Id, CmsPermissions.ContentEdit, cancellationToken);
        await AllowAsync(workbench, bikes.Summary.Id, CmsPermissions.ContentEdit, cancellationToken);

        await using var request = workbench.NewScope();
        var pages = request.ServiceProvider.GetRequiredService<IPageService>();

        var shallow = await pages.PatchMetadataAsync(
            products.Summary.Id,
            new PatchPageMetadataRequest { Title = "Our products" },
            cancellationToken: cancellationToken);

        var deep = await pages.PatchMetadataAsync(
            bikes.Summary.Id,
            new PatchPageMetadataRequest { Title = "Bicycles" },
            cancellationToken: cancellationToken);

        shallow.Outcome.Should().Be(CmsOutcome.Forbidden);
        deep.IsSuccess.Should().BeTrue("the deeper rule beats the shallower one");
    }

    [Test]
    public async Task ARuleOnEditingDoesNotRefusePublishing()
    {
        // Rules are per permission. A deny on Content.Edit that also stopped a publish would make
        // every rule a bigger statement than the person who wrote it made.
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Announcement", cancellationToken);

        await DenyAsync(workbench, page.Summary.Id, CmsPermissions.ContentEdit, cancellationToken);

        await using var request = workbench.NewScope();

        var published = await request.ServiceProvider.GetRequiredService<IPublishingService>()
            .PublishAsync(page.Summary.Id, acknowledgeWarnings: true, cancellationToken);

        published.IsSuccess.Should().BeTrue();
    }

    [Test]
    public async Task AnAdministratorPassesThroughARuleThatWouldRefuseThem()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));
        var page = await workbench.AddPageAsync(template, "Locked down", cancellationToken);

        await DenyAsync(workbench, page.Summary.Id, CmsPermissions.ContentEdit, cancellationToken);

        // The bypass is a property of the caller's roles, not of their permissions, which is why the
        // workbench's authorization stub carries both.
        workbench.Authorization.Roles = [CmsRoles.Administrator];

        await using var request = workbench.NewScope();

        var patched = await request.ServiceProvider.GetRequiredService<IPageService>().PatchMetadataAsync(
            page.Summary.Id,
            new PatchPageMetadataRequest { Title = "Still editable" },
            cancellationToken: cancellationToken);

        patched.IsSuccess.Should().BeTrue("Administrator bypasses access rules — spec section 21.2");
    }

    private static Task AllowAsync(
        PageWorkbench workbench,
        int pageId,
        string permission,
        CancellationToken cancellationToken) =>
        RuleAsync(workbench, pageId, permission, isAllow: true, cancellationToken);

    private static Task DenyAsync(
        PageWorkbench workbench,
        int pageId,
        string permission,
        CancellationToken cancellationToken) =>
        RuleAsync(workbench, pageId, permission, isAllow: false, cancellationToken);

    private static async Task RuleAsync(
        PageWorkbench workbench,
        int pageId,
        string permission,
        bool isAllow,
        CancellationToken cancellationToken)
    {
        workbench.Context.PageAcls.Add(new PageAcl
        {
            PageId = pageId,
            PrincipalType = AclPrincipalType.User,
            PrincipalId = Editor,
            Permission = permission,
            IsAllow = isAllow,
            IsInherited = true,
        });

        await workbench.Context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Walks the tree into a flat list, which is what an assertion about visibility needs.</summary>
    private static IEnumerable<PageTreeNode> Flatten(IEnumerable<PageTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;

            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
