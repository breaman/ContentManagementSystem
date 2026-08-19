using System.Threading.RateLimiting;

using ContentManagementSystem.Core.Preview;
using ContentManagementSystem.Server.Security;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// Registration and mapping for the preview path (tasks P3-16, P3-18 and P3-21).
/// </summary>
public static class PreviewEndpointRouteBuilderExtensions
{
    /// <summary>Route name of the editor's preview, so a test can assert which endpoint matched.</summary>
    public const string EditorPreviewRouteName = "cms-preview";

    /// <summary>Route name of the shared preview.</summary>
    public const string SharedPreviewRouteName = "cms-preview-shared";

    /// <summary>Requests one address may make to the shared preview per window.</summary>
    /// <remarks>
    /// Two per view — the chrome and the frame — so this is roughly thirty page views a minute from
    /// one address. Generous for a reviewer clicking between device widths, and nowhere near enough
    /// to enumerate a 256-bit token space, which is the thing spec section 12.2 asks the limit for.
    /// </remarks>
    public const int SharedRequestsPerWindow = 60;

    /// <summary>Length of the rate-limiting window.</summary>
    public static readonly TimeSpan SharedWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Registers the services the preview endpoints resolve.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsPreviewEndpoint(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCmsPreview();

        // Scoped for the reason CmsPageRenderer is: it is constructed with the request's provider
        // and hands that to the component renderer.
        services.TryAddScoped<CmsPreviewRenderer>();

        services.AddRateLimiter(options =>
        {
            // 429 rather than the default 503. A shared preview link being clicked too fast is the
            // client's problem to slow down about, and 503 tells every intermediary the site itself
            // is unhealthy.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(PreviewEndpoint.SharedRateLimitPolicy, http =>
                RateLimitPartition.GetFixedWindowLimiter(
                    // Partitioned by address, because the population being limited is anonymous by
                    // design — there is no account to key on, which is the whole point of the
                    // feature. An unknown address falls into one shared bucket rather than being
                    // exempt: unlimited is the wrong side to fail on for a link that reads
                    // unpublished content.
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = SharedRequestsPerWindow,
                        Window = SharedWindow,
                        QueueLimit = 0,
                    }));
        });

        return services;
    }

    /// <summary>
    /// Maps the preview routes.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <remarks>
    /// Mapped before the delivery catch-all. <c>/preview</c> is a reserved first segment
    /// (<c>Slugs.Reserved</c>), so no page can be published at one of these addresses and the two can
    /// never be competing for the same URL.
    /// <para>
    /// Every route carries <c>OutputCacheAttribute { NoStore = true }</c>. Output caching itself is
    /// Phase 8 and the middleware is not in the pipeline yet, so the metadata is inert today — which
    /// is exactly why it is added now: the alternative is a policy written months from now that has
    /// to remember preview exists, and it is one line here against an unpublished page in a shared
    /// cache there.
    /// </para>
    /// </remarks>
    public static IEndpointRouteBuilder MapCmsPreview(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var preview = endpoints.MapGroup(PreviewEndpoint.BasePath);

        preview.WithMetadata(new OutputCacheAttribute { NoStore = true });

        // The public policy with one directive changed: frame-ancestors 'self' rather than 'none'
        // (task P9-01). The chrome frames the content to apply a device width to it, and the editing
        // canvas frames the chrome again — both from this origin, which 'none' refuses all the same.
        preview.WithCspProfile(CmsCspProfile.Preview);

        // The shared routes are mapped before the editor ones so that `/preview/s/{token}` is read
        // as a token and never as page id "s" — routing prefers the literal segment, and the order
        // here makes that visible to a reader rather than a fact they have to know.
        var shared = preview.MapGroup(PreviewEndpoint.SharedSegment);

        shared.MapGet("/{token}", PreviewEndpoint.SharedChromeAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PreviewEndpoint.SharedRateLimitPolicy)
            .WithName(SharedPreviewRouteName);

        shared.MapGet($"/{{token}}{PreviewChrome.ContentSegment}", PreviewEndpoint.SharedContentAsync)
            .AllowAnonymous()
            .RequireRateLimiting(PreviewEndpoint.SharedRateLimitPolicy);

        // Content.Read, the permission that already means "may see unpublished content", enforced by
        // a policy rather than only in a service: there is no service call on the refusal path here
        // to make the check in (spec section 21.1).
        preview.MapGet("/{pageId:int}", PreviewEndpoint.EditorChromeAsync)
            .RequireAuthorization(CmsPermissions.ContentRead)
            .WithName(EditorPreviewRouteName);

        preview.MapGet($"/{{pageId:int}}{PreviewChrome.ContentSegment}", PreviewEndpoint.EditorContentAsync)
            .RequireAuthorization(CmsPermissions.ContentRead);

        return endpoints;
    }
}
