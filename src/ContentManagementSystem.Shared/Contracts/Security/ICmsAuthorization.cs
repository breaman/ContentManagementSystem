namespace ContentManagementSystem.Shared.Contracts.Security;

/// <summary>
/// What the caller of the current request is permitted to do.
/// </summary>
/// <remarks>
/// Domain services depend on this rather than on <c>ClaimsPrincipal</c> or ASP.NET Core's
/// <c>IAuthorizationService</c>, which keeps <c>Core</c> free of a web dependency while still
/// letting authorization be enforced where <c>CONTRIBUTING.md</c> requires it — in the service
/// layer, not only at the endpoint. An endpoint policy is a fast rejection at the door; this is the
/// check that still runs when a service is called from a CLI verb, a hosted job, or a second
/// endpoint someone forgot to decorate.
/// <para>
/// Permissions asked about a <em>particular page</em> are <see cref="IAclService"/>, which sits
/// beside this one and is asked after it (spec section 21.2). The split is deliberate: this
/// interface answers from the principal alone and never touches the database, which is what lets it
/// be the check on every operation, including the ones with no page in sight.
/// </para>
/// </remarks>
public interface ICmsAuthorization
{
    /// <summary>Whether the caller holds a global permission.</summary>
    /// <param name="permission">One of the <see cref="CmsPermissions"/> constants.</param>
    /// <returns><see langword="true"/> when the caller may proceed.</returns>
    bool HasPermission(string permission);

    /// <summary>
    /// The role names the caller holds, empty when there is no authenticated caller.
    /// </summary>
    /// <remarks>
    /// Exposed for <see cref="IAclService"/>, which has to find the rules addressed to any of the
    /// caller's roles and cannot ask "are you an <c>Editor</c>" one role at a time without knowing
    /// what roles exist. It is not an invitation to make decisions from role names directly —
    /// <see cref="HasPermission"/> exists so no caller has to.
    /// </remarks>
    IReadOnlyCollection<string> Roles { get; }
}
