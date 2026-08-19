using System.Security.Claims;

using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// Sends a privileged account with no second factor to set one up, and nowhere else
/// (task P9-04, spec section 20.3).
/// </summary>
/// <param name="next">The rest of the pipeline.</param>
/// <param name="options">Which roles require a second factor.</param>
/// <remarks>
/// "Mandatory 2FA for <c>Administrator</c>, <c>Developer</c>, and <c>Approver</c>" is not a setting
/// Identity has. What it has is a flag per user and a sign-in flow that honours it if it is set — so
/// mandatory has to mean that holding one of those roles without the flag leaves nowhere to go except
/// the page that sets it.
/// <para>
/// Enforced on the request rather than in the sign-in flow, deliberately. A check at sign-in refuses
/// an account that is already in this state and leaves it unable to fix itself; and it would say
/// nothing about the account that is granted <c>Administrator</c> while its session is open, which is
/// how most accounts arrive here.
/// </para>
/// <para>
/// It reads a claim rather than the database, so this costs a string comparison rather than a query
/// per request. The cost of that is one refresh: <c>EnableAuthenticator</c> calls
/// <c>RefreshSignInAsync</c> after setting the flag, and without that call the editor who has just
/// finished enrolling would keep being sent back to enrol until the security stamp was revalidated.
/// </para>
/// </remarks>
public sealed class TwoFactorEnrolmentMiddleware(
    RequestDelegate next,
    IOptions<CmsIdentityOptions> options)
{
    /// <summary>Where an account with no second factor is sent.</summary>
    public const string EnrolmentPath = "/Account/Manage/EnableAuthenticator";

    /// <summary>
    /// Paths a half-enrolled account may still reach.
    /// </summary>
    /// <remarks>
    /// Everything under account management, so the enrolment page's own assets, its form post, and the
    /// way out — signing out — all work. Everything under <c>/media</c> and the framework paths,
    /// because a page that cannot load its stylesheet is a page nobody can enrol from. The public site
    /// is not on the list and does not need to be: this only ever applies to a signed-in principal,
    /// and reading published content is not a thing being withheld.
    /// </remarks>
    private static readonly string[] Permitted =
    [
        "/Account",
        "/_framework",
        "/_content",
        "/css",
        "/js",
        "/lib",
        "/media",
        "/health",
        "/alive",
    ];

    private readonly CmsIdentityOptions _options = options.Value;

    /// <summary>
    /// Redirects, or continues.
    /// </summary>
    /// <param name="context">The request.</param>
    /// <returns>A task that completes when the rest of the pipeline has.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is null.</exception>
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!RequiresEnrolment(context.User) || IsPermitted(context.Request.Path))
        {
            return next(context);
        }

        // A redirect for a document request and a 403 for anything else. An API call answered with a
        // redirect to an HTML page is a client that reports a parse error rather than a permission
        // problem, and the backoffice's fetches are the calls most likely to hit this.
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;

            return Task.CompletedTask;
        }

        context.Response.Redirect(EnrolmentPath);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether this principal holds a role that requires a second factor and has not set one up.
    /// </summary>
    /// <param name="user">The signed-in principal.</param>
    /// <returns>Whether it must enrol before doing anything else.</returns>
    private bool RequiresEnrolment(ClaimsPrincipal user) =>
        user.Identity?.IsAuthenticated is true &&
        !user.HasClaim(CmsClaimTypes.TwoFactorEnabled, bool.TrueString) &&
        user.FindAll(ClaimTypes.Role).Any(role => _options.TwoFactorRequiredRoles.Contains(role.Value));

    private static bool IsPermitted(PathString path) =>
        Permitted.Any(permitted => path.StartsWithSegments(permitted, StringComparison.OrdinalIgnoreCase));
}
