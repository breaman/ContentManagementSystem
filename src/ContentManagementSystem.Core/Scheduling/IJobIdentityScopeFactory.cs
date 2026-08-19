using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Scheduling;

/// <summary>
/// Runs background work as a named editor (task P7-13).
/// </summary>
/// <remarks>
/// The sibling of <c>IBulkOperationScopeFactory</c>, and different from it in the one way that
/// matters: a bulk job <em>captures</em> a caller who is still on the phone, while a scheduled job
/// reconstructs one from a user id stored days earlier. The need is identical — every service the
/// job reaches authorizes the caller itself and stamps their identity on an audit row — but there is
/// no ambient principal to copy, so one has to be built.
/// <para>
/// A scheduled publish is still somebody's publish. Running it as nobody would have the publish
/// refused by the service-layer permission check and, if that check were somehow passed, recorded in
/// the audit log as having been done by user 0.
/// </para>
/// </remarks>
public interface IJobIdentityScopeFactory
{
    /// <summary>
    /// Runs one unit of work in a fresh scope in which the given user is the current caller.
    /// </summary>
    /// <param name="userId">The editor the work runs as.</param>
    /// <param name="work">The work, given the scope's services.</param>
    /// <param name="cancellationToken">Token observed by the work.</param>
    /// <remarks>
    /// A scope per unit, so one job's failure leaves no tracked entities behind for the next job to
    /// save by accident.
    /// </remarks>
    Task RunAsAsync(
        int userId,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken);
}

/// <summary>
/// The identity-free implementation, for hosts with no notion of a principal.
/// </summary>
/// <param name="scopes">Factory for the service scopes the work runs in.</param>
/// <remarks>
/// Correct for a CLI verb or a test harness that has substituted its own <c>ICmsAuthorization</c>.
/// It is not correct for the web host, which replaces it — see <c>HttpJobIdentityScopeFactory</c>.
/// Registered anyway so a host that builds this graph without the web layer gets a working service
/// rather than a resolution failure the first time a schedule comes due.
/// </remarks>
public sealed class ServiceScopeJobIdentityScopeFactory(IServiceScopeFactory scopes)
    : IJobIdentityScopeFactory
{
    /// <inheritdoc />
    public async Task RunAsAsync(
        int userId,
        Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var scope = scopes.CreateAsyncScope();

        await work(scope.ServiceProvider, cancellationToken);
    }
}
