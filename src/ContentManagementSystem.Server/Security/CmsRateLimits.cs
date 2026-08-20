using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// The rate limits of spec section 20.6 (task P9-03).
/// </summary>
/// <remarks>
/// Named policies applied to endpoint groups rather than one limiter across the site. A global
/// limiter is a denial-of-service tool pointed at the site's own visitors: it counts the WebAssembly
/// runtime's forty asset requests, the framework's fingerprinted scripts, and the health probe
/// against the same budget as the traffic it is meant to shape, and the first thing it breaks is a
/// cold page load from an office behind one address.
/// <para>
/// <strong>Two of the policies decide per request whether they apply at all</strong>, by returning
/// <see cref="RateLimitPartition.GetNoLimiter{T}"/>. The credentials policy sits on a Razor component
/// endpoint that answers the <c>GET</c> that renders the sign-in form and the <c>POST</c> that
/// attempts it; five of those a quarter of an hour is right for the attempt and absurd for the form,
/// which a single failed sign-in requests twice. The API policy is on the whole versioned group
/// because section 20.6's budget is for writes, and putting it there rather than on each write
/// endpoint means a write added later is covered by default.
/// </para>
/// <para>
/// <strong>Every per-address partition here is only as good as the address.</strong> Behind a reverse
/// proxy or an ingress controller, <c>RemoteIpAddress</c> is the proxy's, and every visitor shares one
/// bucket — which turns the public limit into a site-wide one. The fix is
/// <c>UseForwardedHeaders</c> with <c>KnownProxies</c> or <c>KnownNetworks</c> set to that
/// infrastructure, and it is deliberately not switched on here: forwarded headers trusted from
/// anywhere are a header any client can write, which would let one attacker occupy an unbounded
/// number of buckets. It belongs in the deployment configuration, and is called out in the
/// operations documentation (task P9-19).
/// </para>
/// </remarks>
public static class CmsRateLimits
{
    /// <summary>Sign-in, registration, and password reset: 5 per 15 minutes per address, sliding.</summary>
    public const string Credentials = "cms-credentials";

    /// <summary>Management API writes: 100 a minute per user.</summary>
    public const string ApiWrite = "cms-api-write";

    /// <summary>Media uploads: 20 a minute per user.</summary>
    public const string Upload = "cms-upload";

    /// <summary>Rendition and original delivery: 300 a minute per address.</summary>
    public const string MediaDelivery = "cms-media";

    /// <summary>Public pages: 600 a minute per address.</summary>
    public const string PublicPages = "cms-public";

    /// <summary>Attempts one address may make against a credential endpoint per window.</summary>
    public const int CredentialAttemptsPerWindow = 5;

    /// <summary>The credential window.</summary>
    public static readonly TimeSpan CredentialWindow = TimeSpan.FromMinutes(15);

    /// <summary>Writes one user may make to the management API per minute.</summary>
    public const int ApiWritesPerMinute = 100;

    /// <summary>Uploads one user may start per minute.</summary>
    public const int UploadsPerMinute = 20;

    /// <summary>Media responses one address may fetch per minute.</summary>
    public const int MediaResponsesPerMinute = 300;

    /// <summary>Public pages one address may fetch per minute.</summary>
    public const int PublicPagesPerMinute = 600;

    /// <summary>
    /// The credential routes the sliding window applies to.
    /// </summary>
    /// <remarks>
    /// Matched case-insensitively against the route pattern, because these are Razor component pages
    /// whose <c>@page</c> directives carry the framework's capitalisation and requests arrive with
    /// whatever a link used. Everything that takes a password, a recovery code, a passkey assertion,
    /// or an email address that produces mail is on the list; the confirmation pages that only
    /// display a result are not, because throttling them punishes the person who just succeeded.
    /// </remarks>
    public static readonly string[] CredentialRoutes =
    [
        "/Account/Login",
        "/Account/LoginWith2fa",
        "/Account/LoginWithRecoveryCode",
        "/Account/Register",
        "/Account/ForgotPassword",
        "/Account/ResetPassword",
        "/Account/ResendEmailConfirmation",
        "/Account/PasskeyRequestOptions",
        "/Account/PasskeyCreationOptions",
    ];

    /// <summary>
    /// Registers every policy in spec section 20.6's table.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// Configuration the two public per-address budgets are read from. Omit it and the spec's
    /// figures apply.
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <remarks>
    /// <strong>Only the two public budgets are configurable, and only upwards of nothing.</strong>
    /// They exist because a load test cannot be run against them: NFR-9 asks for five thousand
    /// requests a second and the public budget is ten, so a run from a handful of generators spends
    /// its time measuring the rejection path (task P9-13). Raising them is a deployment's decision
    /// about its own environment, and the defaults are section 20.6's numbers, so an environment
    /// that configures nothing is limited exactly as the spec says.
    /// <para>
    /// The credential and API budgets are deliberately <em>not</em> configurable. They are the ones
    /// protecting passwords and writes, and no load test needs them moved.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddCmsRateLimiting(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var limits = new CmsRateLimitOptions();

        configuration?.GetSection(CmsRateLimitOptions.SectionName).Bind(limits);

        limits.Validate();

        services.AddRateLimiter(options =>
        {
            // 429 rather than the default 503. A client going too fast is the client's problem to
            // slow down about, and 503 tells every intermediary the site itself is unhealthy.
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = static (context, cancellationToken) => Refuse(context, cancellationToken);

            // Sliding rather than fixed, which section 20.6 asks for by name and which matters here
            // more than anywhere else: a fixed window lets ten attempts through in the seconds either
            // side of a boundary, and ten is twice the budget.
            options.AddPolicy(Credentials, http => Post(http)
                ? RateLimitPartition.GetSlidingWindowLimiter(
                    Address(http),
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = CredentialAttemptsPerWindow,
                        Window = CredentialWindow,
                        SegmentsPerWindow = 5,
                        QueueLimit = 0,
                    })
                : Unlimited);

            options.AddPolicy(ApiWrite, http => Write(http)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    Caller(http),
                    _ => Minute(ApiWritesPerMinute))
                : Unlimited);

            options.AddPolicy(Upload, http =>
                RateLimitPartition.GetFixedWindowLimiter(Caller(http), _ => Minute(UploadsPerMinute)));

            options.AddPolicy(MediaDelivery, http =>
                RateLimitPartition.GetFixedWindowLimiter(Address(http), _ => Minute(limits.MediaResponsesPerMinute)));

            options.AddPolicy(PublicPages, http =>
                RateLimitPartition.GetFixedWindowLimiter(Address(http), _ => Minute(limits.PublicPagesPerMinute)));
        });

        return services;
    }

    /// <summary>
    /// Applies the credential limit to the account pages among a set of endpoints.
    /// </summary>
    /// <typeparam name="TBuilder">The convention builder.</typeparam>
    /// <param name="builder">The endpoints, typically every Razor component page.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// A convention that reads each endpoint's route rather than a call on each page, because the
    /// pages are mapped by <c>MapRazorComponents</c> from <c>@page</c> directives and there is no
    /// per-page builder to hang anything off. The trade is that a typo in
    /// <see cref="CredentialRoutes"/> silently limits nothing, which is why the test suite asserts
    /// every entry matches a real endpoint.
    /// </remarks>
    public static TBuilder RequireCmsCredentialRateLimiting<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint =>
        {
            if (endpoint is RouteEndpointBuilder route &&
                CredentialRoutes.Any(credential => Matches(route, credential)))
            {
                endpoint.Metadata.Add(new EnableRateLimitingAttribute(Credentials));
            }
        });

        return builder;
    }

    /// <summary>Whether a route pattern is one of the credential routes.</summary>
    /// <param name="route">The endpoint being built.</param>
    /// <param name="credential">The route to compare against.</param>
    /// <returns>Whether they name the same path.</returns>
    private static bool Matches(RouteEndpointBuilder route, string credential) =>
        string.Equals(
            $"/{route.RoutePattern.RawText?.TrimStart('/')}",
            credential,
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Writes the refusal: <c>Retry-After</c>, and a body.
    /// </summary>
    /// <param name="context">The rejected request and its lease.</param>
    /// <param name="cancellationToken">Token observed while the body is written.</param>
    /// <returns>A task that completes when the body has been written.</returns>
    /// <remarks>
    /// <strong>The body is what stops the refusal being rewritten as a 404.</strong> The site's
    /// status-code pages re-execute any error response that carries no body, and the page they
    /// re-execute through is the not-found page, which sets its own status — so a body-less 429
    /// reaches the client as "no such page", losing both the reason and the <c>Retry-After</c> that
    /// would have told it when to come back. The same pathology cost preview and the API their own
    /// exclusions in <c>Program</c>; here it is cheaper to answer properly than to add a third.
    /// <para>
    /// A problem document under <c>/api</c> and a sentence anywhere else. An API client parses one
    /// shape for every refusal this application makes, and a person who has clicked too fast reads
    /// the other in a browser window.
    /// </para>
    /// </remarks>
    private static async ValueTask Refuse(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;

        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : (int)TimeSpan.FromMinutes(1).TotalSeconds;

        response.Headers[HeaderNames.RetryAfter] = seconds.ToString(CultureInfo.InvariantCulture);

        var detail = $"Too many requests. Try again in {seconds} second(s).";

        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            response.ContentType = "application/problem+json";

            await response.WriteAsJsonAsync(
                new
                {
                    type = "https://cms.example/errors/rate-limited",
                    title = "Too many requests.",
                    status = StatusCodes.Status429TooManyRequests,
                    detail,
                },
                cancellationToken);

            return;
        }

        response.ContentType = "text/plain; charset=utf-8";

        await response.WriteAsync(detail, cancellationToken);
    }

    /// <summary>A one-minute fixed window of the given size, refusing rather than queueing.</summary>
    /// <param name="permits">Requests allowed in the window.</param>
    /// <returns>The options.</returns>
    /// <remarks>
    /// <c>QueueLimit = 0</c> throughout. Queueing a request that is over budget holds a connection
    /// open and turns a limiter into a place to accumulate load, which is the opposite of the point.
    /// </remarks>
    private static FixedWindowRateLimiterOptions Minute(int permits) =>
        new()
        {
            PermitLimit = permits,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        };

    /// <summary>The partition for a request the policy does not apply to.</summary>
    private static RateLimitPartition<string> Unlimited => RateLimitPartition.GetNoLimiter("none");

    private static bool Post(HttpContext http) => HttpMethods.IsPost(http.Request.Method);

    /// <summary>Whether this request changes anything.</summary>
    /// <param name="http">The request.</param>
    /// <returns>Whether it is a write.</returns>
    /// <remarks>
    /// By exclusion rather than by listing the write verbs, so a method nobody thought of counts as a
    /// write. Reads are cheap, cacheable, and already bounded by what an authenticated editor can see.
    /// </remarks>
    private static bool Write(HttpContext http) =>
        !HttpMethods.IsGet(http.Request.Method) &&
        !HttpMethods.IsHead(http.Request.Method) &&
        !HttpMethods.IsOptions(http.Request.Method);

    /// <summary>
    /// The signed-in user, or the address if there is not one.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <returns>The partition key.</returns>
    /// <remarks>
    /// The two are prefixed apart. Without that a user whose id is <c>10.0.0.1</c> — impossible
    /// today, and one schema change away from possible — would share a bucket with an address.
    /// </remarks>
    private static string Caller(HttpContext http) =>
        http.User.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } id
            ? $"user:{id}"
            : $"addr:{Address(http)}";

    /// <summary>
    /// The requesting address.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <returns>The partition key.</returns>
    /// <remarks>
    /// An unknown address falls into one shared bucket rather than being exempt. Unlimited is the
    /// wrong side to fail on for a limiter whose whole population is anonymous.
    /// </remarks>
    private static string Address(HttpContext http) =>
        http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
