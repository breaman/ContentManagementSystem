using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// Registration, pipeline placement, and per-endpoint profile selection for the response security
/// headers (tasks P9-01 and P9-02).
/// </summary>
public static class CmsSecurityHeadersExtensions
{
    /// <summary>
    /// Registers the nonce and the policy strings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration the options bind from.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static IServiceCollection AddCmsSecurityHeaders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<CmsSecurityHeaderOptions>(
            configuration.GetSection(CmsSecurityHeaderOptions.SectionName));

        // Scoped is what makes "per request" true (ADR-0013). A singleton would hand every visitor
        // the same value for the lifetime of the process, which is a constant wearing a nonce's name.
        services.TryAddScoped<ICspNonce, CspNonce>();

        // Singleton: the policies are strings assembled from options that do not change while the
        // process runs, and they are read on every response.
        services.TryAddSingleton<CmsContentSecurityPolicy>();

        return services;
    }

    /// <summary>
    /// Adds the security headers to every response.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <remarks>
    /// Place this after routing — the endpoint's profile metadata is not resolved before it — and
    /// before output caching, so a response served from the cache still gets a header written for
    /// this request rather than one recorded when the page was rendered.
    /// </remarks>
    public static IApplicationBuilder UseCmsSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<CmsSecurityHeadersMiddleware>();
    }

    /// <summary>
    /// Serves these endpoints under a policy other than the public one.
    /// </summary>
    /// <typeparam name="TBuilder">The convention builder.</typeparam>
    /// <param name="builder">The endpoints.</param>
    /// <param name="profile">The profile.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// There is deliberately no way to opt <em>into</em> the public policy: it is what a route gets
    /// for saying nothing. A new endpoint is strict until somebody decides otherwise in writing.
    /// </remarks>
    public static TBuilder WithCspProfile<TBuilder>(this TBuilder builder, CmsCspProfile profile)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.WithMetadata(new CmsCspProfileMetadata(profile));

        return builder;
    }
}
