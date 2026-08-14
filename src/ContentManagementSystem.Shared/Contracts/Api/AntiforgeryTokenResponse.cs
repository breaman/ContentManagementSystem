namespace ContentManagementSystem.Shared.Contracts.Api;

/// <summary>
/// Body of <c>GET /api/cms/v1/antiforgery-token</c>.
/// </summary>
/// <param name="HeaderName">Header the request token must be sent in.</param>
/// <param name="RequestToken">The request token to echo back on writes.</param>
/// <remarks>
/// The header name is returned rather than hard-coded in the client, so changing it is a server-side
/// configuration change and not a coordinated deployment.
/// <para>
/// In <c>Shared</c> because both ends need it: the server issues the pair, and the WebAssembly
/// backoffice — which cannot reference the server project — has to read it before it can save
/// anything at all.
/// </para>
/// </remarks>
public sealed record AntiforgeryTokenResponse(string HeaderName, string RequestToken);

/// <summary>Antiforgery settings shared by the server configuration and its clients.</summary>
public static class CmsAntiforgeryDefaults
{
    /// <summary>Header the management API reads the request token from.</summary>
    public const string HeaderName = "X-CSRF-TOKEN";
}
