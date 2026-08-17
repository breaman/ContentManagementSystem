using System.Security.Claims;

using ContentManagementSystem.Core.Content;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Carries the signed-in editor's identity into a bulk job that outlives their request
/// (task P6-29).
/// </summary>
/// <param name="accessor">Access to the current request, and through it the caller's principal.</param>
/// <param name="scopes">Factory for the service scopes each item runs in.</param>
/// <remarks>
/// <c>HttpCmsAuthorization</c> answers "no request, no permissions", which is the right default and
/// exactly the problem here: item forty runs long after the response was written, and every service
/// it reaches authorizes the caller itself. So the caller is captured while there is still one to
/// capture, and each item's scope is given a synthetic request carrying that same principal.
/// <para>
/// A synthetic <see cref="HttpContext"/> rather than a second notion of identity, because two of
/// them would be two things to keep in step: <c>IUserService</c> reads the name-identifier claim to
/// stamp audit rows, and <c>ICmsAuthorization</c> reads the role claims to allow the write. Both
/// read the ambient context, so giving them one is how they both keep working — and how the audit
/// trail says who published the forty pages rather than saying nobody did.
/// </para>
/// </remarks>
public sealed class HttpBulkOperationScopeFactory(
    IHttpContextAccessor accessor,
    IServiceScopeFactory scopes) : IBulkOperationScopeFactory
{
    /// <inheritdoc />
    public ICapturedCaller CaptureCaller() =>
        new CapturedPrincipal(accessor.HttpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity()), scopes);

    /// <summary>One captured principal, and the scopes run on its behalf.</summary>
    /// <param name="principal">Who asked for the batch.</param>
    /// <param name="scopes">Factory for the service scopes each item runs in.</param>
    private sealed class CapturedPrincipal(ClaimsPrincipal principal, IServiceScopeFactory scopes)
        : ICapturedCaller
    {
        /// <inheritdoc />
        public async Task RunAsync(
            Func<IServiceProvider, CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(work);

            await using var scope = scopes.CreateAsyncScope();

            // Set inside the scope and before the work resolves anything: IHttpContextAccessor is
            // backed by an AsyncLocal, so the assignment flows into everything awaited below and into
            // nothing outside this method.
            var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();

            accessor.HttpContext = new DefaultHttpContext
            {
                User = principal,
                RequestServices = scope.ServiceProvider,
            };

            try
            {
                await work(scope.ServiceProvider, cancellationToken);
            }
            finally
            {
                // Cleared rather than left standing. The flow is this method's own, so nothing else
                // would see it, but a context whose scope has been disposed is a trap for anything
                // that later reads RequestServices off it.
                accessor.HttpContext = null;
            }
        }
    }
}
