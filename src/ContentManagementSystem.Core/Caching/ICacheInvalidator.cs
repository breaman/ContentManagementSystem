namespace ContentManagementSystem.Core.Caching;

/// <summary>
/// Evicts cache entries carrying the given tags (task P8-10, spec section 16.2).
/// </summary>
/// <remarks>
/// The abstraction exists because the two caches being evicted live at different levels of the
/// application: the output cache is an ASP.NET Core middleware store, and the published-content
/// cache is a <c>HybridCache</c> the domain services read through. Core enqueues and dispatches;
/// the host supplies the implementation that knows about both.
/// <para>
/// <strong>Eviction is idempotent.</strong> Evicting a tag nothing carries is a no-op, which is what
/// lets the outbox dispatch at least once rather than exactly once — and every instance apply the
/// same message to its own in-process cache.
/// </para>
/// </remarks>
public interface ICacheInvalidator
{
    /// <summary>
    /// Evicts everything carrying any of these tags.
    /// </summary>
    /// <param name="tags">The tags.</param>
    /// <param name="cancellationToken">Token observed while evicting.</param>
    Task InvalidateAsync(IReadOnlyCollection<string> tags, CancellationToken cancellationToken = default);
}
