using System.Diagnostics;

using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Workflow;

/// <summary>
/// The content tree stays fast with access rules applied (task P7-26, risk R15).
/// </summary>
/// <remarks>
/// R15 is the risk that permission resolution becomes the reason the tree is slow — a query per
/// node, ten levels deep, on the screen an editor looks at all day. The mitigation is structural
/// rather than incidental: the rules that bear on one caller are read once per request and every
/// node after that is a string prefix comparison in memory (task P7-05).
/// <para>
/// The budget is generous and the assertion is deliberately not a benchmark. What it catches is the
/// regression that matters — somebody moving the check inside the loop — which shows up as hundreds
/// of round trips and blows any budget on any machine. A wall-clock number tuned finely enough to
/// measure anything else would fail on a busy CI agent for reasons nobody could act on.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class AclPerformanceTests(SqlServerFixture fixture)
{
    /// <summary>How deep the tree under test goes, from task P7-26.</summary>
    private const int Depth = 10;

    /// <summary>Children at each level, so the tree is a tree rather than a list.</summary>
    private const int Breadth = 3;

    /// <summary>The budget, from task P7-26.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);

    [Test]
    public async Task TheTreeLoadsWithinBudgetAtDepthTenWithRulesApplied()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var workbench = await PageWorkbench.CreateAsync(fixture, cancellationToken: cancellationToken);

        var template = await workbench.UseTemplateAsync("article", cancellationToken, PageWorkbench.TextZone("body"));

        var root = await workbench.AddPageAsync(template, "Root", cancellationToken);
        var frontier = new List<int> { root.Summary.Id };
        var everyPage = new List<int> { root.Summary.Id };

        for (var level = 1; level < Depth; level++)
        {
            var next = new List<int>();

            foreach (var parent in frontier)
            {
                for (var sibling = 0; sibling < Breadth; sibling++)
                {
                    var page = await workbench.AddPageAsync(
                        template,
                        $"Level {level} node {sibling}",
                        cancellationToken,
                        parent);

                    next.Add(page.Summary.Id);
                    everyPage.Add(page.Summary.Id);
                }
            }

            // Only the deepest branch is extended, which keeps the fixture buildable while still
            // producing a page at depth ten with nine ancestors above it — which is what the
            // inheritance rules have to walk.
            frontier = [next[0]];
        }

        // Rules at several depths, so the resolver has real precedence to work out rather than a
        // single row it could shortcut.
        for (var i = 0; i < everyPage.Count; i += 4)
        {
            workbench.Context.PageAcls.Add(new PageAcl
            {
                PageId = everyPage[i],
                PrincipalType = AclPrincipalType.User,
                PrincipalId = 1,
                Permission = CmsPermissions.ContentRead,
                IsAllow = i % 8 == 0,
                IsInherited = true,
            });
        }

        await workbench.Context.SaveChangesAsync(cancellationToken);

        await using var request = workbench.NewScope();
        var pages = request.ServiceProvider.GetRequiredService<IPageService>();

        // Warmed first, so the measurement is of the query rather than of EF compiling it. The
        // regression this guards against — a query per node — is not something a warm-up hides.
        await pages.TreeAsync(null, depth: Depth, cancellationToken);

        var stopwatch = Stopwatch.StartNew();
        var tree = await pages.TreeAsync(null, depth: Depth, cancellationToken);
        stopwatch.Stop();

        tree.IsSuccess.Should().BeTrue();
        stopwatch.Elapsed.Should().BeLessThan(
            Budget,
            "a per-node permission query at depth {0} is what risk R15 is about",
            Depth);
    }
}
