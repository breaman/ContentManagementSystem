using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Carries the caller's identity out of the request that started a bulk job (task P6-29).
/// </summary>
/// <remarks>
/// A background batch outlives the request that asked for it, and everything it runs — publishing,
/// deleting, patching metadata — authorizes the caller itself and stamps their identity on an audit
/// row. A job that ran without one would either be refused on the first item or, far worse, be
/// recorded as having been done by nobody.
/// <para>
/// The abstraction is here rather than the mechanism because the mechanism is web-shaped:
/// <c>HttpCmsAuthorization</c> and <c>HttpUserService</c> both read the ambient
/// <c>HttpContext</c>, which is gone by the time item forty runs. <c>Core</c> states what it needs —
/// a service scope in which the person who pressed the button is still the current user — and the
/// hosting layer says how that is arranged.
/// </para>
/// </remarks>
public interface IBulkOperationScopeFactory
{
    /// <summary>
    /// Captures whoever is asking, for use after their request has ended.
    /// </summary>
    /// <returns>A handle that runs work as that caller.</returns>
    /// <remarks>
    /// Called on the request thread, while there is still a caller to capture. Calling it from the
    /// background task instead would capture nothing, which is exactly the bug this interface exists
    /// to make impossible to write by accident.
    /// </remarks>
    ICapturedCaller CaptureCaller();
}

/// <summary>
/// A caller, captured, and the ability to run work as them.
/// </summary>
public interface ICapturedCaller
{
    /// <summary>
    /// Runs one unit of work in a fresh service scope owned by the captured caller.
    /// </summary>
    /// <param name="work">The work, given the scope's services.</param>
    /// <param name="cancellationToken">Token observed by the work.</param>
    /// <remarks>
    /// A scope per unit rather than one for the batch, and the reason is failure isolation: each item
    /// gets its own database context, so an item that fails leaves no tracked entities behind for the
    /// next one to save by accident, and a batch of 400 does not accumulate 400 items' worth of
    /// change tracking.
    /// </remarks>
    Task RunAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken cancellationToken);
}

/// <summary>
/// The identity-free implementation, for hosts that have no ambient caller to lose.
/// </summary>
/// <param name="scopes">Factory for the service scopes the work runs in.</param>
/// <remarks>
/// Correct for a CLI verb or a test harness, where whatever <c>ICmsAuthorization</c> answers in a
/// bare scope is the same thing it answers everywhere else. It is <em>not</em> correct for the web
/// host, which replaces it — see <c>HttpBulkOperationScopeFactory</c>. Registered here anyway so a
/// host that builds this graph without the web layer gets a working service rather than a resolution
/// error at the moment somebody presses "publish branch".
/// </remarks>
public sealed class ServiceScopeBulkOperationScopeFactory(IServiceScopeFactory scopes)
    : IBulkOperationScopeFactory, ICapturedCaller
{
    /// <inheritdoc />
    public ICapturedCaller CaptureCaller() => this;

    /// <inheritdoc />
    public async Task RunAsync(
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = scopes.CreateAsyncScope();

        await work(scope.ServiceProvider, cancellationToken);
    }
}
