namespace ContentManagementSystem.Server.Services;

/// <summary>
/// Runs the pre-render shims' service calls one at a time within a request (ADR-0022).
/// </summary>
/// <remarks>
/// <strong>Blazor starts sibling components' asynchronous lifecycle methods concurrently.</strong>
/// A render batch that brings four field editors into the tree calls
/// <c>OnInitializedAsync</c> on all four; each runs to its first <c>await</c> and the rest of them
/// then overlap. In the browser that is free — every call is its own HTTP request. While
/// pre-rendering it is not: the <c>Server*Client</c> shims call the services directly, and every
/// service in the request shares one scoped <c>ApplicationDbContext</c>. A second query starting
/// while the first is still reading is what EF Core reports as "a second operation was started on
/// this context instance", and it took down the page editor, which draws one editor per zone.
/// <para>
/// The gate serializes those calls instead of scattering an <c>IDbContextFactory</c> through the
/// service layer. It is the smaller change and it costs nothing real: the renderer is already
/// running this work on one logical thread, so the calls were never going to overlap usefully — they
/// were only ever going to collide. Screens still paint in one batch, just with their queries queued
/// rather than interleaved.
/// </para>
/// <para>
/// Scoped, so the gate and the context it protects have the same lifetime, and so the HTTP API is
/// untouched: each API request is its own scope with its own gate and its own context, and nothing
/// queues behind anything.
/// </para>
/// <para>
/// <strong>Never call a gated method from inside a gated operation.</strong> The semaphore is not
/// reentrant, so a shim calling another shim would wait on itself forever. The shims call services,
/// and services do not call shims, which is what keeps that from arising.
/// </para>
/// </remarks>
public sealed class PrerenderGate : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Runs one service call, once whatever is already running has finished.
    /// </summary>
    /// <typeparam name="T">What the call answers.</typeparam>
    /// <param name="operation">The call, given the token to observe while it waits its turn.</param>
    /// <param name="cancellationToken">Abandons the wait as well as the call.</param>
    /// <returns>Whatever the call answered.</returns>
    /// <example>
    /// <code>
    /// public async Task&lt;IReadOnlyList&lt;BlockTypeSummary&gt;&gt; GetBlockTypesAsync(
    ///     CancellationToken cancellationToken = default) =&gt;
    ///     (await gate.RunAsync(token =&gt; blockTypes.ListAsync(token), cancellationToken)).Value ?? [];
    /// </code>
    /// </example>
    public async Task<T> RunAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await gate.WaitAsync(cancellationToken);

        try
        {
            return await operation(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();
}
