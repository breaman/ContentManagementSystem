using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// Registration and pipeline placement for the sign-in hardening of spec section 20.3 (task P9-04).
/// </summary>
public static class CmsIdentityHardeningExtensions
{
    /// <summary>
    /// The registration routes, refused when self-service registration is off.
    /// </summary>
    /// <remarks>
    /// The confirmation page is on the list too: a deployment with registration disabled that still
    /// serves <c>/Account/RegisterConfirmation</c> tells a caller the feature exists and then refuses
    /// the one page that does anything. <c>ResendEmailConfirmation</c> is deliberately not — an
    /// account an administrator created still has an address to confirm, and that is the page it
    /// confirms it from.
    /// </remarks>
    public static readonly string[] RegistrationRoutes =
    [
        "/Account/Register",
        "/Account/RegisterConfirmation",
    ];

    /// <summary>
    /// Registers the password screens, the extra validator, and the options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Configuration the options bind from.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public static IServiceCollection AddCmsIdentityHardening(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<CmsIdentityOptions>(configuration.GetSection(CmsIdentityOptions.SectionName));

        services.TryAddSingleton<CommonPasswordScreen>();

        var identity = new CmsIdentityOptions();
        configuration.GetSection(CmsIdentityOptions.SectionName).Bind(identity);

        if (identity.UseHaveIBeenPwned)
        {
            services.AddHttpClient(HaveIBeenPwnedScreen.HttpClientName, client =>
            {
                client.BaseAddress = new Uri(HaveIBeenPwnedScreen.BaseAddress);

                // Short on purpose. This is on the path of a password change, and a service that has
                // not answered in three seconds is one this request should stop waiting for — what
                // happens then is RefuseWhenBreachServiceUnavailable's decision, not a timeout's.
                client.Timeout = TimeSpan.FromSeconds(3);

                // The API asks callers to identify themselves, and pads its responses when asked.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("ContentManagementSystem/1.0");
                client.DefaultRequestHeaders.Add("Add-Padding", "true");
            });

            services.TryAddSingleton<IBreachedPasswordScreen>(provider => new HaveIBeenPwnedScreen(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(HaveIBeenPwnedScreen.HttpClientName),
                provider.GetRequiredService<CommonPasswordScreen>(),
                provider.GetRequiredService<ILogger<HaveIBeenPwnedScreen>>(),
                provider.GetRequiredService<IOptions<CmsIdentityOptions>>().Value
                    .RefuseWhenBreachServiceUnavailable));
        }
        else
        {
            services.TryAddSingleton<IBreachedPasswordScreen>(
                provider => provider.GetRequiredService<CommonPasswordScreen>());
        }

        // Added to Identity's own validators rather than replacing them: length and the character-set
        // rules stay Identity's job, and these two are the ones it has no setting for.
        services.AddScoped<IPasswordValidator<User>, CmsPasswordValidator>();

        return services;
    }

    /// <summary>
    /// Applies the password policy of spec section 20.3 to Identity's own options.
    /// </summary>
    /// <param name="options">The Identity options being configured.</param>
    /// <param name="identity">The CMS's own settings.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// <strong>Length, and no character classes.</strong> The template's defaults were six characters
    /// with every class rule off; section 20.3 asks for twelve. The class rules stay off, and that is
    /// a decision rather than an omission: requiring a digit, a capital, and a symbol is what produces
    /// <c>Password1!</c> — a twelve-character passphrase is stronger than anything those rules can
    /// force, and the breach screen catches the passphrases everyone else also thought of.
    /// </remarks>
    public static void ApplyCmsPasswordPolicy(this IdentityOptions options, CmsIdentityOptions identity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(identity);

        options.Password.RequiredLength = identity.MinimumPasswordLength;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 4;

        // Five attempts then a five-minute lock, applied to everybody including administrators. The
        // template excludes them, which is the one account an attacker is definitely trying.
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;

        options.SignIn.RequireConfirmedAccount = true;
    }

    /// <summary>
    /// Refuses the registration routes when self-service registration is off.
    /// </summary>
    /// <typeparam name="TBuilder">The convention builder.</typeparam>
    /// <param name="builder">The endpoints, typically every Razor component page.</param>
    /// <param name="policy">The configured policy.</param>
    /// <returns>The builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// 404 rather than 403, for the reason a refused <c>Content.Read</c> answers not found: a 403 that
    /// a 404 would not have produced tells the caller the door is there.
    /// <para>
    /// A filter rather than declining to map the routes, because these come from <c>@page</c>
    /// directives and there is nothing to decline. The trade is the same one
    /// <see cref="CmsRateLimits.RequireCmsCredentialRateLimiting{T}"/> makes, and the same test
    /// covers it: every route in the list has to name a real endpoint.
    /// </para>
    /// </remarks>
    public static TBuilder RefuseSelfRegistrationWhenDisabled<TBuilder>(
        this TBuilder builder,
        SelfRegistrationPolicy policy)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (policy is not SelfRegistrationPolicy.Disabled)
        {
            return builder;
        }

        builder.Add(endpoint =>
        {
            if (endpoint is not RouteEndpointBuilder route ||
                !RegistrationRoutes.Any(registration => string.Equals(
                    $"/{route.RoutePattern.RawText?.TrimStart('/')}",
                    registration,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // The page's own delegate is replaced rather than wrapped behind a flag. A route that
            // can be re-enabled by a request header is not disabled.
            endpoint.RequestDelegate = context =>
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;

                return Task.CompletedTask;
            };
        });

        return builder;
    }

    /// <summary>
    /// Gates a privileged account that has not set up a second factor.
    /// </summary>
    /// <param name="app">The application.</param>
    /// <returns>The application, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is null.</exception>
    /// <remarks>
    /// After authentication, which is where the principal it reads comes from, and before anything
    /// that serves content to one.
    /// </remarks>
    public static IApplicationBuilder UseCmsTwoFactorEnrolment(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<TwoFactorEnrolmentMiddleware>();
    }
}
