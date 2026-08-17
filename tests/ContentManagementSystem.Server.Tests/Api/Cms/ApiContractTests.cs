using System.Reflection;

using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Server.Authorization;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Api.Cms;

/// <summary>
/// Structural rules the whole management API surface has to keep (tasks P2-21 and P2-22).
/// </summary>
/// <remarks>
/// Asserted over the route table the application actually builds rather than against a list somebody
/// maintains here. The failure being prevented is a new endpoint added without a policy, or one that
/// binds a type a client can use to set a column no endpoint is supposed to expose — neither of which
/// any single-endpoint test would notice, because the endpoint in question does not exist yet when
/// its test is written.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class ApiContractTests(SqlServerFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Columns that decide what the public site serves, or whether a row exists at all.
    /// </summary>
    /// <remarks>
    /// A write DTO carrying any of these would let a client move a page's lifecycle through an
    /// ordinary edit — <c>{"status": "Published"}</c> on a metadata patch being the case spec
    /// section 20.1 names. Every one of them is reachable only through a dedicated endpoint with its
    /// own permission.
    /// </remarks>
    private static readonly string[] ForbiddenWriteMembers =
    [
        "Status",
        "IsDeleted",
        "DeletedOn",
        "DeletedBy",
        "PublishedOn",
        "PublishedBy",
        "PublishedVersionId",
        "DraftVersionId",
        "VersionNumber",
        "Path",
        "Depth",
        "IsBuiltIn",
        "CurrentRevision",
    ];

    private CmsApplicationFactory _factory = null!;

    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public void EveryPermissionHasAPolicyAndEveryPolicyHasAPermission()
    {
        var declared = typeof(CmsPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        // The map is what both the endpoint policies and the service-layer checks read, so a
        // permission missing from it is one whose policy would admit nobody while its service check
        // silently refuses everybody — an endpoint that looks registered and cannot be called.
        declared.Should().BeEquivalentTo(CmsPermissionMap.RolesByPermission.Keys);

        CmsPermissionMap.RolesByPermission.Values
            .SelectMany(roles => roles)
            .Distinct()
            .Should().OnlyContain(role => KnownRoles.Contains(role));
    }

    [Fact]
    public async Task EveryPermissionsPolicyIsRegistered()
    {
        var policies = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        foreach (var permission in CmsPermissionMap.RolesByPermission.Keys)
        {
            var policy = await policies.GetPolicyAsync(permission);

            policy.Should().NotBeNull($"'{permission}' is used by RequireAuthorization on an endpoint");
        }
    }

    [Fact]
    public void PhaseTwosPermissionsGrantWhatTheSpecTableSays()
    {
        // Spec section 21.1, transcribed as an assertion rather than as a second copy of the table:
        // these are the grants Phase 2's endpoints depend on being right.
        RolesFor(CmsPermissions.ContentRead).Should().Contain(CmsRoles.Viewer);
        RolesFor(CmsPermissions.ContentEdit).Should().Contain(CmsRoles.Author)
            .And.NotContain(CmsRoles.Viewer);
        RolesFor(CmsPermissions.ContentPublish).Should().NotContain(CmsRoles.Author);
        RolesFor(CmsPermissions.ContentDelete).Should().NotContain(CmsRoles.Author)
            .And.NotContain(CmsRoles.Approver);
        RolesFor(CmsPermissions.StructureEdit).Should().BeEquivalentTo(
            [CmsRoles.Administrator, CmsRoles.Developer]);
        RolesFor(CmsPermissions.SettingsEdit).Should().BeEquivalentTo(
            [CmsRoles.Administrator, CmsRoles.Developer]);

        // The permanent delete is the one irreversible operation in the system, so it sits with user
        // management rather than with content deletion (task P2-17).
        RolesFor(CmsPermissions.UsersManage).Should().BeEquivalentTo([CmsRoles.Administrator]);
    }

    [Fact]
    public void TheBackofficesRoleListsMatchThePermissionsTheyStandInFor()
    {
        // The screens run in WebAssembly, where the server's permission policies do not exist, so
        // [Authorize(Roles = …)] is all they can say. These lists are a convenience and a hazard in
        // equal measure: a role added to the map and not to the list gets a blank screen instead of
        // the page they are entitled to, and nothing else would notice.
        Roles(CmsRoles.ContentReaders).Should().BeEquivalentTo(RolesFor(CmsPermissions.ContentRead));
        Roles(CmsRoles.ContentEditors).Should().BeEquivalentTo(RolesFor(CmsPermissions.ContentEdit));
        Roles(CmsRoles.ContentPublishers).Should().BeEquivalentTo(RolesFor(CmsPermissions.ContentPublish));
        Roles(CmsRoles.StructureEditors).Should().BeEquivalentTo(RolesFor(CmsPermissions.StructureEdit));
    }

    [Fact]
    public void EveryCmsEndpointRequiresAnAuthorizationPolicy()
    {
        var unprotected = new List<string>();

        foreach (var endpoint in CmsEndpoints())
        {
            var authorize = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

            // The group requires an authenticated user, which is the floor. A named policy on top is
            // what makes the endpoint answer 403 rather than 200 to a Viewer.
            if (!authorize.Any(data => !string.IsNullOrEmpty(data.Policy)))
            {
                unprotected.Add(Route(endpoint));
            }
        }

        // Two exemptions, both of them about the caller rather than about content.
        //
        // The token endpoint is how an authenticated caller obtains the antiforgery pair every write
        // then needs, and gating it behind a content permission would mean a Viewer could never make
        // a request that requires one.
        //
        // "/me" reports the caller's own identity and nothing else. A content permission is the wrong
        // gate for it — a media manager with no content rights still has a name and an id, and the
        // answer discloses nothing the caller did not arrive holding. The floor the group applies,
        // an authenticated user, is the whole requirement.
        unprotected.Should().BeEquivalentTo(
        [
            $"{CmsApiEndpoints.BasePath}/antiforgery-token",
            $"{CmsApiEndpoints.BasePath}/me",
        ]);
    }

    [Fact]
    public void EveryWriteEndpointRequiresAnAntiforgeryToken()
    {
        var forgeable = CmsEndpoints()
            .Where(IsWrite)
            .Where(endpoint => endpoint.Metadata.GetMetadata<CmsAntiforgeryMetadata>() is null)
            .Select(Route)
            .ToList();

        // The API is cookie-authenticated, so a write without this is one any page a signed-in
        // editor visits can make on their behalf.
        forgeable.Should().BeEmpty();
    }

    [Fact]
    public void NoWriteEndpointBindsATypeThatCouldMoveAPagesLifecycle()
    {
        var offences = new List<string>();

        foreach (var endpoint in CmsEndpoints().Where(IsWrite))
        {
            var handler = endpoint.Metadata.GetMetadata<MethodInfo>();

            if (handler is null) continue;

            foreach (var parameter in handler.GetParameters().Where(IsBoundFromBody))
            {
                var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

                if (!type.Namespace?.StartsWith("ContentManagementSystem.Shared.Contracts", StringComparison.Ordinal)
                    ?? true)
                {
                    offences.Add($"{Route(endpoint)} binds {type.FullName}, which is not a request contract");

                    continue;
                }

                foreach (var member in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (ForbiddenWriteMembers.Contains(member.Name, StringComparer.Ordinal))
                    {
                        offences.Add($"{Route(endpoint)} binds {type.Name}.{member.Name}");
                    }
                }
            }
        }

        // Status transitions go through publish, unpublish, delete, and restore — each with its own
        // permission and its own transaction. A DTO that accepted one as data would route around all
        // four (spec section 20.1).
        offences.Should().BeEmpty();
    }

    private static bool IsWrite(RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
            .Any(method => method is "POST" or "PUT" or "PATCH" or "DELETE") ?? false;

    /// <summary>
    /// Whether a handler parameter is bound from the request body.
    /// </summary>
    /// <remarks>
    /// Minimal APIs infer this: a complex type that the container cannot supply and that carries no
    /// explicit binding attribute comes from the body. The container is asked rather than guessed at,
    /// because the alternative — matching on namespace or on "is an interface" — would quietly stop
    /// covering a handler the day somebody injects a concrete service.
    /// <para>
    /// The framework's own request abstractions are not bound from the body — they <em>are</em> the
    /// request. A handler taking one is reading the body itself, which the redirect CSV import does
    /// because what an operator has is a file rather than a JSON document. That escapes the DTO
    /// shape check below, and legitimately so: there is no contract type for this rule to inspect,
    /// and a handler that parses CSV cannot bind a lifecycle column by accident. What still covers
    /// it is the antiforgery and permission assertions above, which every write endpoint faces
    /// whatever it binds.
    /// </para>
    /// </remarks>
    private bool IsBoundFromBody(ParameterInfo parameter)
    {
        var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(CancellationToken) ||
            typeof(HttpContext).IsAssignableFrom(type) ||
            typeof(HttpRequest).IsAssignableFrom(type))
        {
            return false;
        }

        var isService = _factory.Services.GetRequiredService<IServiceProviderIsService>();

        return !isService.IsService(type);
    }

    private static string Route(RouteEndpoint endpoint) => $"/{endpoint.RoutePattern.RawText?.TrimStart('/')}";

    /// <summary>Splits an <c>[Authorize(Roles = …)]</c> list into its role names.</summary>
    private static IReadOnlyList<string> Roles(string list) =>
        list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IReadOnlyList<string> RolesFor(string permission) =>
        CmsPermissionMap.RolesByPermission[permission];

    private IEnumerable<RouteEndpoint> CmsEndpoints() =>
        _factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => Route(endpoint)
                .StartsWith(CmsApiEndpoints.BasePath, StringComparison.Ordinal));

    private static readonly string[] KnownRoles =
    [
        CmsRoles.Administrator, CmsRoles.Developer, CmsRoles.Editor, CmsRoles.Author,
        CmsRoles.Approver, CmsRoles.MediaManager, CmsRoles.Viewer,
    ];
}
