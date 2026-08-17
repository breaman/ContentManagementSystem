using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Shared.Services;

/// <summary>
/// Tells a backoffice screen who is signed in (task P6-17).
/// </summary>
/// <remarks>
/// Implemented twice, exactly as <see cref="IPageClient"/> is: over <c>HttpClient</c> in the
/// WebAssembly backoffice and directly over the request principal on the server, so a screen
/// pre-renders knowing the answer instead of asking for it after hydration.
/// </remarks>
public interface ICurrentUserClient
{
    /// <summary>Reads the signed-in editor's identity.</summary>
    /// <param name="cancellationToken">Token observed while loading.</param>
    /// <returns>Who is signed in, or null when nobody is.</returns>
    Task<CurrentUser?> GetAsync(CancellationToken cancellationToken = default);
}
