using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Core.Security;

/// <summary>
/// Registration helpers for HTML sanitization.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SanitizationService"/> as the deployment's <see cref="IContentSanitizer"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional deployment policy — frameable hosts, class allowlist, inline image cap.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Call this before <c>AddCmsFieldTypes()</c> resolves: the <c>richText</c> and <c>html</c> field
    /// types take an <see cref="IContentSanitizer"/>, so a container without one fails to build the
    /// field type registry. That failure is the intended behaviour and this is the fix for it — the
    /// alternative to failing at startup is a deployment that quietly stores hostile markup.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddCmsSanitization(options =>
    /// {
    ///     options.AllowedCssClasses.Add("lead");
    ///     options.AllowedIframeHosts.Add("fast.wistia.net");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddCmsSanitization(
        this IServiceCollection services,
        Action<SanitizationOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SanitizationOptions();

        configure?.Invoke(options);

        // Registered as the built instance rather than through IOptions: the profiles are read once
        // at construction into three prepared sanitizers, so a policy that could change after
        // startup would be a policy that silently does not apply.
        services.TryAddSingleton<IContentSanitizer>(_ => new SanitizationService(options));

        return services;
    }
}
