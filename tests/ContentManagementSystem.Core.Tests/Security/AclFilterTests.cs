using ContentManagementSystem.Shared.Contracts.Security;

using FluentAssertions;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// How access rules resolve: inheritance, depth precedence, deny over allow, and the administrator
/// bypass (task P7-21, spec section 21.2).
/// </summary>
/// <remarks>
/// Driven against <see cref="AclFilter"/> directly rather than through the database, because the
/// precedence rules are arithmetic over a handful of rows and nothing about them needs a container.
/// The resolver's other half — which rows bear on which caller — is asserted in the integration
/// suite, where it has an identity and a tree to be wrong about.
/// <para>
/// The paths here are the real thing: <c>Page.Path</c> is a materialized ancestor chain like
/// <c>/1/8/44/</c>, and inheritance is the prefix relation between two of them.
/// </para>
/// </remarks>
public class AclFilterTests
{
    private const string Products = "/1/";
    private const string Bikes = "/1/8/";
    private const string Frames = "/1/8/44/";
    private const string About = "/2/";

    [Test]
    public void ARuleReachesEveryDescendantOfThePageItIsAttachedTo()
    {
        var filter = new AclFilter([Allow(1, Products, depth: 0)]);

        filter.Allows(1, Products).Should().BeTrue();
        filter.Allows(8, Bikes).Should().BeTrue();
        filter.Allows(44, Frames).Should().BeTrue("inheritance is a prefix match, not one level");
    }

    [Test]
    public void OneAllowAnywhereRefusesEverywhereElse()
    {
        // The mechanism by which an ACL narrows rather than only widens (criterion P7 #5). An editor
        // given /products is thereby refused /about, or the rule would grant nothing it did not
        // already have.
        var filter = new AclFilter([Allow(1, Products, depth: 0)]);

        filter.Allows(2, About).Should().BeFalse();
    }

    [Test]
    public void ADenyOnItsOwnLeavesEverythingElseAlone()
    {
        var filter = new AclFilter([Deny(2, About, depth: 0)]);

        filter.Allows(2, About).Should().BeFalse();
        filter.Allows(1, Products).Should().BeTrue("a deny narrows one branch, not the site");
    }

    [Test]
    public void ADeeperRuleBeatsAShallowerOne()
    {
        var filter = new AclFilter([Allow(1, Products, depth: 0), Deny(8, Bikes, depth: 1)]);

        filter.Allows(1, Products).Should().BeTrue();
        filter.Allows(8, Bikes).Should().BeFalse();
        filter.Allows(44, Frames).Should().BeFalse("the deny is inherited by what is below it");
    }

    [Test]
    public void ADeeperAllowReopensABranchInsideADeny()
    {
        var filter = new AclFilter([Deny(1, Products, depth: 0), Allow(44, Frames, depth: 2)]);

        filter.Allows(8, Bikes).Should().BeFalse();
        filter.Allows(44, Frames).Should().BeTrue();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public void DenyBeatsAllowAtTheSameDepthWhicheverOrderTheRowsArrivedIn(bool denyFirst)
    {
        // Two rules on one page — a user allow and a role deny, say. The answer must not depend on
        // the order the query returned them in, which is the whole reason this case is asserted
        // both ways round.
        AclRule[] rules = denyFirst
            ? [Deny(8, Bikes, depth: 1), Allow(8, Bikes, depth: 1)]
            : [Allow(8, Bikes, depth: 1), Deny(8, Bikes, depth: 1)];

        new AclFilter(rules).Allows(8, Bikes).Should().BeFalse();
    }

    [Test]
    public void ARuleThatDoesNotInheritGovernsItsOwnPageAlone()
    {
        var filter = new AclFilter(
        [
            Deny(1, Products, depth: 0),
            new AclRule(8, Bikes, Depth: 1, IsAllow: true, IsInherited: false),
        ]);

        filter.Allows(8, Bikes).Should().BeTrue("the hole is punched in exactly one page");
        filter.Allows(44, Frames).Should().BeFalse("and not in the branch below it");
    }

    [Test]
    public void ACallerNoRuleMentionsKeepsWhateverTheirRolesGaveThem()
    {
        var filter = AclFilter.Unrestricted;

        filter.IsUnrestricted.Should().BeTrue();
        filter.Allows(44, Frames).Should().BeTrue();
    }

    [Test]
    public void AnAdministratorIsAllowedThroughARuleThatWouldRefuseThem()
    {
        var filter = new AclFilter([Deny(1, Products, depth: 0)], isBypassed: true);

        filter.Allows(8, Bikes).Should().BeTrue();

        // And the bypass is reported, because spec section 21.2 asks for a record of it and a
        // filter that merely said "yes" would leave nothing to log.
        filter.WouldRefuseWithoutBypass(8, Bikes).Should().BeTrue();
    }

    [Test]
    public void AnAdministratorNoRuleRefusesIsNotReportedAsBypassingAnything()
    {
        // Logging every administrator request would bury the ones that mean something. A bypass only
        // does anything when a rule was going to refuse.
        var filter = new AclFilter([Deny(2, About, depth: 0)], isBypassed: true);

        filter.WouldRefuseWithoutBypass(1, Products).Should().BeFalse();
    }

    private static AclRule Allow(int pageId, string path, int depth) =>
        new(pageId, path, depth, IsAllow: true, IsInherited: true);

    private static AclRule Deny(int pageId, string path, int depth) =>
        new(pageId, path, depth, IsAllow: false, IsInherited: true);
}
