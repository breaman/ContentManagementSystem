using System.Diagnostics;

using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Media;
using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Server.Authorization;
using ContentManagementSystem.Server.Cli;
using ContentManagementSystem.Server.Delivery;
using ContentManagementSystem.Server.Delivery.Preview;
using ContentManagementSystem.Server.HealthChecks;
using ContentManagementSystem.Server.Media;
using ContentManagementSystem.Server.Components;
using ContentManagementSystem.Server.Components.Account;
using ContentManagementSystem.Server.Components.Email;
using ContentManagementSystem.Server.Services;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.ServiceDefaults;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Serilog;

Serilog.Debugging.SelfLog.Enable(msg => Debug.WriteLine(msg));

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

Log.Information("Starting up");

var isMigrations = Environment.GetCommandLineArgs()[0].Contains("ef.dll");

try
{
    var builder = WebApplication.CreateBuilder(args);

    if (!isMigrations)
    {
        builder.Host.UseSerilog((ctx, lc) => lc
            .ReadFrom.Configuration(ctx.Configuration));
    }

    builder.AddServiceDefaults();

    builder.Services.AddRazorComponents()
        .AddInteractiveWebAssemblyComponents()
        .AddAuthenticationStateSerialization();

    builder.Services.AddCascadingAuthenticationState();
    builder.Services.AddScoped<IdentityRedirectManager>();

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        })
        .AddIdentityCookies();
    builder.Services.AddAuthorization();

    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseSqlServer(builder.Configuration.GetConnectionString(Constants.DatabaseConnectionString))
            .EnableSensitiveDataLogging());
    builder.EnrichSqlServerDbContext<ApplicationDbContext>();
    builder.Services.AddDatabaseDeveloperPageExceptionFilter();

    // The media blob store is provisioned by Aspire (Azurite in development). Registration is
    // conditional so hosts that never touch media — notably the API integration test harness —
    // do not fail their health check on a storage account they were never given.
    if (!string.IsNullOrWhiteSpace(
            builder.Configuration.GetConnectionString(Constants.MediaBlobConnectionString)))
    {
        builder.AddAzureBlobServiceClient(Constants.MediaBlobConnectionString);
    }

    builder.Services.AddIdentityCore<User>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 6;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;

            // options.SignIn.RequireConfirmedEmail = true;
            options.SignIn.RequireConfirmedAccount = true;

            options.Stores.SchemaVersion = IdentitySchema.Version;
        })
        // AddRoles isn't added from the AddIdentityCore, so if you want to use roles, this must be explicitly added
        .AddRoles<Role>()
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddSignInManager()
        .AddDefaultTokenProviders()
        .AddClaimsPrincipalFactory<CustomUserClaimsPrincipalFactory>();

    // The CMS spine (task P1-30). Order is immaterial, but the dependencies are not: the field
    // types resolve an IContentSanitizer, so a deployment that registered them without
    // AddCmsSanitization would fail to build the registry rather than quietly store raw markup.
    builder.Services.AddCmsSanitization();
    builder.Services.AddCmsFieldTypes();
    builder.Services.AddCmsStructure();
    builder.Services.AddCmsAuthorization();
    // The payload engine (task P1-30, completed in P2-10). This is what P1-30 was waiting for: the
    // catalog it registers is DatabaseContentSchemaCatalog, which reads captured revision snapshots
    // and caches them for the life of the process. Registering an empty catalog earlier, to make
    // startup succeed, would have produced a deployment that validated every payload against
    // nothing — worse than not validating at all, because it reports success.
    builder.Services.AddCmsContent();
    // Pages, drafts, versions, publishing, and the recycle bin (tasks P2-05 to P2-15). Scoped,
    // unlike the stateless halves of the payload engine, because these hold a database context.
    builder.Services.AddCmsPages();
    // URLs, redirects, and route resolution (tasks P3-04 and P3-05). Registered beside the page
    // services rather than with delivery, because it is the write path that depends on it: creating,
    // renaming, publishing, and recycling a page all rebuild routes inside their own transactions.
    builder.Services.AddCmsRouting();
    // The read-only public delivery path (tasks P3-12 and P3-13): the published-content service and
    // the component renderer the catch-all endpoint serves pages through.
    builder.Services.AddCmsDeliveryEndpoint();
    // Preview (tasks P3-16 to P3-19). It renders through the delivery pipeline registered above and
    // differs only in which version it loads, which is what makes preview fidelity structural
    // (spec section 12.1). Also registers the rate limiter the shared-link routes require.
    builder.Services.AddCmsPreviewEndpoint();

    // The media library (tasks P5-03 to P5-09). The store follows the same condition the blob client
    // registration above uses: a host given a storage account uses it, and one without — the API
    // integration harness, a developer without Docker — falls back to the local disk under a root
    // outside wwwroot. The two are interchangeable to everything above IMediaStore.
    builder.Services.AddCmsMedia();

    // The signing key is a secret and belongs in user secrets or a key vault, never in
    // appsettings.json: anyone holding it can make this server encode arbitrary renditions, which is
    // the denial of service the signature exists to prevent (spec section 20.8). A host with none
    // configured generates a per-process key and says so loudly.
    builder.Services.AddCmsMediaDelivery(options =>
        builder.Configuration.GetSection(MediaSigningOptions.SectionName).Bind(options));

    if (!string.IsNullOrWhiteSpace(
            builder.Configuration.GetConnectionString(Constants.MediaBlobConnectionString)))
    {
        builder.Services.AddCmsBlobMediaStore();
    }
    else
    {
        builder.Services.AddCmsFileSystemMediaStore(
            Path.Combine(builder.Environment.ContentRootPath, new MediaStorageOptions().FileSystemRoot));
    }

    // The CMS's own meter and activity source (task P2-29, spec section 24.1). Registering the
    // instruments is not enough on its own: an unlisted meter records measurements that no exporter
    // ever collects, which looks identical to code that was never instrumented.
    builder.Services.AddOpenTelemetry()
        .WithMetrics(metrics => metrics.AddMeter(CmsTelemetry.MeterName))
        .WithTracing(tracing => tracing.AddSource(CmsTelemetry.ActivitySourceName));

    // Which assemblies declare [CmsTemplate] and [CmsBlockType] (task P1-25). Named rather than
    // discovered from the loaded assembly list: the scan has to give the same answer under a trimmed
    // publish as it does under `dotnet run`, and it should not walk every framework assembly.
    builder.Services.AddCmsStructureReconciliation(
        typeof(ContentManagementSystem.Rendering.RenderingAssemblyMarker).Assembly,
        typeof(Program).Assembly);

    // The rendering pipeline (task P3-08). Its component catalog reads the same scan the line above
    // configures, which is what keeps the key a page stores, the row the reconciler writes, and the
    // component that renders it from ever being three different answers.
    builder.Services.AddCmsRendering();

    builder.Services.Configure<SchemaSyncOptions>(
        builder.Configuration.GetSection(SchemaSyncOptions.SectionName));

    // The cms-templates check of spec section 24.2. Degraded, never unhealthy: a bad deployment must
    // be visible without taking down a site whose pages still render.
    builder.Services.AddHealthChecks()
        .AddCheck<CmsTemplatesHealthCheck>(
            CmsTemplatesHealthCheck.Name,
            tags: ["ready", "cms"])
        // The media store round trip of spec section 24.2. Unhealthy rather than degraded: an
        // unwritable store means no upload succeeds and no cold rendition can be generated.
        .AddCheck<CmsMediaStoreHealthCheck>(
            CmsMediaStoreHealthCheck.Name,
            tags: ["ready", "cms", "media"]);

    // The management API is cookie-authenticated, so every write carries an antiforgery token in a
    // header. Naming the header here is what lets the JSON endpoints validate one at all — the
    // default configuration only reads the token from a form field.
    builder.Services.AddAntiforgery(options => options.HeaderName = CmsAntiforgeryDefaults.HeaderName);

    // Runs the reconciliation and then the schema sync, in that order, once at startup.
    builder.Services.AddHostedService<CmsStructureStartupService>();

    // Builds the render-path catalogs while starting, so a component or renderer that cannot be
    // resolved is a deployment-time failure rather than a page-time one (task P3-09).
    builder.Services.AddHostedService<CmsRenderingStartupService>();

    builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();
    builder.Services.AddScoped<IUserService, HttpUserService>();

    // Backs the structure admin screens while they pre-render, calling the services directly rather
    // than looping back through the HTTP API (task P1-29).
    builder.Services.AddScoped<IStructureClient, ServerStructureClient>();
    builder.Services.AddScoped<IPageClient, ServerPageClient>();
    builder.Services.AddScoped<IReusableClient, ServerReusableClient>();
    builder.Services.AddScoped<IMediaClient, ServerMediaClient>();
    builder.Services.AddScoped<IToastService, ToastService>();

    // The shell's layout store, which can do nothing here: static rendering has no JavaScript, so it
    // answers with the default geometry and the browser restores the editor's own on hydration.
    builder.Services.AddScoped<IShellLayoutStore, BrowserShellLayoutStore>();

    // Add route configuration to enforce lowercase URLs for better SEO
    builder.Services.Configure<RouteOptions>(options =>
    {
        options.LowercaseUrls = true;
        options.LowercaseQueryStrings = true;
        options.AppendTrailingSlash = false;
    });

    var app = builder.Build();

    // Proves the image library can encode everything it claims to (task P5-09). SkiaSharp answers an
    // unsupported encode with null rather than an exception, so without this a native build missing
    // the WebP encoder would serve empty image responses and log nothing (spec section 13.9.1).
    app.Services.AssertCmsMediaCapabilities();

    // `dotnet run -- cms schema ...` (task P1-28). Handled after Build so the verbs use exactly the
    // services the site uses, and before anything is mapped so no request pipeline is ever started.
    if (CmsCommandLine.Handles(args))
    {
        return await CmsCommandLine.RunAsync(app, args);
    }

    app.MapDefaultEndpoints();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.UseWebAssemblyDebugging();
        app.UseMigrationsEndPoint();
    }
    else
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    // Status-code pages give the public site an HTML error experience by re-executing the request
    // against a Razor page. The API is excluded: a 403 from the authorization middleware carries no
    // body, so it qualifies for re-execution, and re-running a JSON POST through a component
    // endpoint replaced it with an unrelated 400 about the content type. An API client must see the
    // status its request actually produced.
    //
    // Preview is excluded for the same reason, one surface along. It writes its own documents for
    // every refusal — "this link has expired", "this preview is no longer available" — which are the
    // whole of what a stakeholder with no account has to go on, and re-executing a body-less 403
    // through the site's error page reported it as a 404 besides.
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments(CmsApiEndpoints.ApiPathPrefix) &&
                   !context.Request.Path.StartsWithSegments(PreviewEndpoint.BasePath),
        branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    // After authorization, so the limiter reads the endpoint's policy metadata. Only the shared
    // preview routes opt in; everything else is unlimited, because a limiter in front of the whole
    // site is a denial-of-service tool pointed at its own visitors.
    app.UseRateLimiter();

    app.MapStaticAssets();

    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(ContentManagementSystem.Client._Imports).Assembly);

    app.MapAdditionalIdentityEndpoints();
    app.MapCmsApi();

    // Before the catch-all, like every other route. /preview is a reserved first segment
    // (Slugs.Reserved), so no page can ever be published at one of these addresses.
    app.MapCmsPreview();

    // Signed renditions and stored originals (tasks P5-14 to P5-17). /media is reserved for the
    // same reason, and every byte of media the site emits goes through here rather than through
    // static file middleware — which is what makes content-type pinning and nosniff universal
    // rather than per-path (spec section 20.7).
    app.MapCmsMedia();

    // Last, and it must stay last (task P3-13, spec section 15.1). This is the catch-all that serves
    // every content URL, and anything mapped after it is a route a visitor could shadow with a page
    // slug. The P3-15 route-ordering tests assert the outcome — /api, /admin, /account, /health, and
    // the Blazor framework paths all still reach their own endpoints — so a reshuffle fails loudly
    // rather than quietly (risk R6).
    app.MapCmsDelivery();

    app.Run();

    return 0;
}
catch (Exception ex) when (ex.GetType().Name is not "StopTheHostException" &&
                           ex.GetType().Name is not "HostAbortedException")
{
    Log.Fatal(ex, "Unhandled exception.");

    // A non-zero code so a container orchestrator, and `dotnet run -- cms …`, both see the failure.
    return 1;
}
finally
{
    Log.Information("Shut down complete");
    Log.CloseAndFlush();
}

/// <summary>
/// Entry point for the server host.
/// </summary>
/// <remarks>
/// Top-level statements generate an internal <c>Program</c> class. Declaring it public here lets
/// <c>WebApplicationFactory&lt;Program&gt;</c> in the integration test suite boot the real
/// application rather than a hand-assembled approximation of it.
/// </remarks>
public partial class Program;