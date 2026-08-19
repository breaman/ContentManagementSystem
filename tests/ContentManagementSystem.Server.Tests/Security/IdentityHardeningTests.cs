using System.Net;

using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Security;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The sign-in hardening of spec section 20.3 (task P9-04).
/// </summary>
/// <remarks>
/// Four rules that used to be template defaults: a twelve-character minimum, a breach screen, a
/// second factor for the roles that can change what the public site says, and a front door that is
/// shut until somebody answers <strong>Q10</strong>.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class IdentityHardeningTests(SqlServerFixture fixture)
{
    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task ThePasswordPolicyIsTwelveCharactersAndLocksOutEverybody()
    {
        var options = _bench.Resolve<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.Should().Be(12);

        // Off deliberately rather than by omission: requiring a digit, a capital, and a symbol is
        // what produces Password1!, and the breach screen is what catches the results.
        options.Password.RequireDigit.Should().BeFalse();
        options.Password.RequireNonAlphanumeric.Should().BeFalse();

        // The template excludes new users from lockout, which is the one account an attacker is
        // definitely trying.
        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);

        options.SignIn.RequireConfirmedAccount.Should().BeTrue();

        await Task.CompletedTask;
    }

    [Test]
    [Arguments("password123", true)]
    [Arguments("P@ssword-123", true)]
    [Arguments("PASSWORD123", true)]
    [Arguments("correcthorsebatterystaple", true)]
    [Arguments("qwertyuiop", true)]
    [Arguments("Marmalade-Tricycle-Ninety", false)]
    [Arguments("a-quiet-hill-in-october", false)]
    public async Task TheCommonScreenSeesThroughTheUsualManglings(string password, bool breached)
    {
        var screen = new CommonPasswordScreen();

        (await screen.IsBreachedAsync(password, TestContext.Current!.Execution.CancellationToken))
            .Should().Be(breached);
    }

    [Test]
    public async Task APasswordBuiltFromTheAccountIsRefused()
    {
        var manager = _bench.Resolve<UserManager<User>>();
        var validator = new CmsPasswordValidator(new CommonPasswordScreen());

        var user = new User { UserName = "rowan@contoso.example", Email = "rowan@contoso.example" };

        var refused = await validator.ValidateAsync(manager, user, "rowan-is-my-name-2026");

        refused.Succeeded.Should().BeFalse();
        refused.Errors.Should().ContainSingle()
            .Which.Code.Should().Be(CmsPasswordValidator.ContainsIdentityCode);

        // The domain half is not matched: half the organisation shares it, so it would refuse
        // everything or nothing depending on the domain.
        var accepted = await validator.ValidateAsync(manager, user, "contoso-is-not-in-here");

        accepted.Succeeded.Should().BeTrue();
    }

    [Test]
    public async Task ABreachedPasswordIsRefusedWithACodeAClientCanActOn()
    {
        var manager = _bench.Resolve<UserManager<User>>();
        var validator = new CmsPasswordValidator(new CommonPasswordScreen());

        var result = await validator.ValidateAsync(
            manager,
            new User { UserName = "rowan", Email = "rowan@contoso.example" },
            "correcthorsebatterystaple");

        result.Succeeded.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(CmsPasswordValidator.BreachedCode);
    }

    [Test]
    public async Task RegistrationIsClosedUntilQ10IsAnswered()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient(followRedirects: false);

        foreach (var route in CmsIdentityHardeningExtensions.RegistrationRoutes)
        {
            using var response = await client.GetAsync(route, cancellationToken);

            // Not found rather than forbidden, for the reason a refused Content.Read answers not
            // found: a 403 a 404 would not have produced tells the caller the door is there.
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{route} is closed by default");
        }

        // And nothing else on the account surface went with it.
        using var login = await client.GetAsync("/Account/Login", cancellationToken);

        login.StatusCode.Should().Be(HttpStatusCode.OK);

        using var resend = await client.GetAsync("/Account/ResendEmailConfirmation", cancellationToken);

        // Deliberately still open: an account an administrator created has an address to confirm.
        resend.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task EveryRegistrationRouteNamesAnEndpointThatExists()
    {
        var routes = _bench.Resolve<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var route in CmsIdentityHardeningExtensions.RegistrationRoutes)
        {
            routes.Should().Contain(route);
        }

        await Task.CompletedTask;
    }

    [Test]
    public async Task AnAdministratorWithNoSecondFactorCanReachEnrolmentAndNothingElse()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient(followRedirects: false);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, CmsRoles.Administrator);
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoTwoFactorHeader, "true");

        using var admin = await client.GetAsync("/admin", cancellationToken);

        admin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        admin.Headers.Location!.OriginalString
            .Should().Be(TwoFactorEnrolmentMiddleware.EnrolmentPath);

        // A redirect to an HTML page is a parse error rather than a permission problem to the
        // backoffice's fetches, so the API is refused outright instead.
        using var api = await client.GetAsync("/api/cms/v1/me", cancellationToken);

        api.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The way out has to stay open, or the account cannot fix itself — and in particular the
        // enrolment page must not be redirected to the enrolment page, which is the failure this
        // whole middleware is one mistake away from.
        using var enrol = await client.GetAsync(TwoFactorEnrolmentMiddleware.EnrolmentPath, cancellationToken);

        enrol.Headers.Location?.OriginalString
            .Should().NotBe(TwoFactorEnrolmentMiddleware.EnrolmentPath);

        using var signIn = await client.GetAsync("/Account/Login", cancellationToken);

        signIn.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AnAuthorIsNotAskedForASecondFactor()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient(followRedirects: false);
        client.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, CmsRoles.Author);
        client.DefaultRequestHeaders.Add(TestAuthHandler.NoTwoFactorHeader, "true");

        // Section 20.3 names three roles, and the rule is only worth having if it is the three. An
        // Author cannot publish and cannot grant anybody else anything.
        using var response = await client.GetAsync("/api/cms/v1/me", cancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task AnAnonymousVisitorIsNeverGated()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _bench.CreateClient(followRedirects: false);

        using var response = await client.GetAsync("/robots.txt", cancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
