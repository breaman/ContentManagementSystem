using System.Diagnostics;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Core.Auditing;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Caching;
using ContentManagementSystem.Core.Dashboard;
using ContentManagementSystem.Core.Delivery.Seo;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Media;
using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Core.Navigation;
using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Core.Routing;
using ContentManagementSystem.Core.Scheduling;
using ContentManagementSystem.Core.Search;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Core.Workflow;
using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interceptors;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Rendering;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Server.Authorization;
using ContentManagementSystem.Server.Caching;
using ContentManagementSystem.Server.Cli;
using ContentManagementSystem.Server.Delivery;
using ContentManagementSystem.Server.Delivery.Preview;
using ContentManagementSystem.Server.Delivery.Seo;
using ContentManagementSystem.Server.HealthChecks;
using ContentManagementSystem.Server.HostedServices;
using ContentManagementSystem.Server.Media;
using ContentManagementSystem.Server.Security;
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

    // Soft-delete rewriting, fingerprint stamping, and audit capture. They are the context's
    // save-time behaviour and are registered onto its options below, so a context built without
    // them saves silently doing none of it — see CmsSaveInterceptors.
    builder.Services.AddCmsSaveInterceptors();

    // AddDbContextFactory rather than AddDbContext, and scoped rather than the default singleton
    // (ADR-0022). Since EF Core 6 the factory registration also registers the context itself as a
    // scoped service, so every service taking an ApplicationDbContext is unaffected; what it adds is
    // an IDbContextFactory for the delivery readers, which run inside one render and cannot share a
    // context. Scoped is load-bearing twice over: it keeps DbContextOptions scoped, which is the
    // lifetime EnrichSqlServerDbContext preserves when it patches the descriptor, and it gives the
    // factory a scoped provider to build contexts from — a singleton factory could not resolve the
    // scoped IUserService the save interceptors read the caller from.
    builder.Services.AddDbContextFactory<ApplicationDbContext>(
        (services, options) => options
            .UseSqlServer(builder.Configuration.GetConnectionString(Constants.DatabaseConnectionString))
            .EnableSensitiveDataLogging()
            .AddInterceptors(CmsSaveInterceptors.Resolve(services)),
        ServiceLifetime.Scoped);
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
    // Section-level access rules (tasks P7-04, P7-05). Registered beside the global permission
    // policies because the two are asked together: a role grant says what an editor may do, and
    // this says where.
    builder.Services.AddCmsAccessControl();
    // The payload engine (task P1-30, completed in P2-10). This is what P1-30 was waiting for: the
    // catalog it registers is DatabaseContentSchemaCatalog, which reads captured revision snapshots
    // and caches them for the life of the process. Registering an empty catalog earlier, to make
    // startup succeed, would have produced a deployment that validated every payload against
    // nothing — worse than not validating at all, because it reports success.
    builder.Services.AddCmsContent();
    // Pages, drafts, versions, publishing, and the recycle bin (tasks P2-05 to P2-15). Scoped,
    // unlike the stateless halves of the payload engine, because these hold a database context.
    builder.Services.AddCmsPages();
    // The landing screen's four tiles (tasks P6-24 to P6-27). It reads across pages, media,
    // references, the audit log, and the not-found log without owning any of them, which is why it
    // is registered beside the page services rather than by them.
    builder.Services.AddCmsDashboard();
    // Review, comments, notifications, mail, and scheduling (tasks P7-09 to P7-19). One call,
    // because these depend on each other: workflow raises notifications, notifications need a
    // transport, and a scheduled publish notifies its owner about what it did.
    builder.Services.AddCmsWorkflow();
    // The read-only audit viewer (task P7-20). Registered separately because it depends on nothing
    // the workflow does — it reads rows the save interceptor writes.
    builder.Services.AddCmsAuditing();
    builder.Services.Configure<CmsEmailOptions>(
        builder.Configuration.GetSection(CmsEmailOptions.SectionName));
    builder.Services.Configure<PublishSchedulerOptions>(
        builder.Configuration.GetSection(PublishSchedulerOptions.SectionName));
    // URLs, redirects, and route resolution (tasks P3-04 and P3-05). Registered beside the page
    // services rather than with delivery, because it is the write path that depends on it: creating,
    // renaming, publishing, and recycling a page all rebuild routes inside their own transactions.
    builder.Services.AddCmsRouting();
    // The read-only public delivery path (tasks P3-12 and P3-13): the published-content service and
    // the component renderer the catch-all endpoint serves pages through.
    builder.Services.AddCmsDeliveryEndpoint();
    // The public address the canonical links, Open Graph URLs, and sitemap are written against, and
    // the sitemap's own shape (tasks P8-01 and P8-04). Everything an editor decides lives on the
    // page or in SiteSettings; what is left here is what only the deployment can know.
    builder.Services.Configure<SeoOptions>(
        builder.Configuration.GetSection(SeoOptions.SectionName));

    // The published-content and route caches, the invalidation queue, and the outbox runner
    // (tasks P8-08 to P8-10). Registered after delivery and routing, because two of these are
    // decorators over registrations those calls made.
    // Structural navigation and managed menus (tasks P8-15, P8-16). Read-only, and registered
    // beside delivery because the public site is what renders them.
    builder.Services.AddCmsNavigation();

    builder.Services.AddCmsCaching();
    builder.Services.Configure<DeliveryCacheOptions>(
        builder.Configuration.GetSection(DeliveryCacheOptions.SectionName));
    builder.Services.Configure<OutboxOptions>(
        builder.Configuration.GetSection(OutboxOptions.SectionName));

    // The output cache itself, and Redis behind it when a deployment has one (tasks P8-06, P8-11).
    // UseOutputCache is placed after UseAuthentication below, which is what lets the policy see the
    // principal and refuse to cache an authenticated response (spec section 16.4).
    builder.Services.AddCmsOutputCache(builder.Configuration);

    // Dispatches what publishing enqueued, on every instance rather than on one (task P8-09).
    builder.Services.AddHostedService<OutboxProcessorService>();

    // Backoffice search and the tag vocabulary (tasks P8-18 to P8-20). Registered after caching,
    // whose outbox runner dispatches the index messages this adds a handler for.
    builder.Services.AddCmsSearch();
    builder.Services.Configure<SearchOptions>(
        builder.Configuration.GetSection(SearchOptions.SectionName));

    // The nightly repair pass that makes asynchronous indexing safe to rely on (risk R18).
    builder.Services.AddHostedService<SearchReconcileService>();
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
            tags: ["ready", "cms", "media"])
        // The cms-scheduler check of task P7-17. Unhealthy when publishing is more than five
        // minutes behind, or when the poll loop has stopped altogether — the second of which
        // otherwise has no symptom until somebody notices a page that never went live.
        .AddCheck<CmsSchedulerHealthCheck>(
            CmsSchedulerHealthCheck.Name,
            tags: ["ready", "cms", "scheduler"])
        // The cms-outbox check of task P8-13. The failure it exists for has no other symptom: when
        // invalidation stops draining, every request still succeeds and every page still renders,
        // with content that was replaced hours ago.
        .AddCheck<CmsOutboxHealthCheck>(
            CmsOutboxHealthCheck.Name,
            tags: ["ready", "cms", "cache"]);

    // The management API is cookie-authenticated, so every write carries an antiforgery token in a
    // header. Naming the header here is what lets the JSON endpoints validate one at all — the
    // default configuration only reads the token from a form field.
    builder.Services.AddAntiforgery(options => options.HeaderName = CmsAntiforgeryDefaults.HeaderName);

    // Runs the reconciliation and then the schema sync, in that order, once at startup.
    builder.Services.AddHostedService<CmsStructureStartupService>();

    // Builds the render-path catalogs while starting, so a component or renderer that cannot be
    // resolved is a deployment-time failure rather than a page-time one (task P3-09).
    builder.Services.AddHostedService<CmsRenderingStartupService>();

    // Which component an author fills each field type in with, and the startup check that every
    // registered field type has one (ADR-0014, tasks P6-06 to P6-15). This is the only place both
    // halves are in scope: the catalog is in Client and the registry is in Core.
    builder.Services.AddSingleton<IFieldEditorCatalog>(new FieldEditorCatalog());
    builder.Services.AddHostedService<CmsEditorStartupService>();

    // Identity's account mail goes through the same transport as workflow notifications (task
    // P7-18). The no-op sender it replaces discarded password resets silently.
    builder.Services.AddSingleton<IEmailSender<User>, IdentityCmsEmailSender>();
    builder.Services.AddScoped<IUserService, HttpUserService>();

    // A bulk job outlives the request that started it, and everything it runs authorizes the caller
    // and stamps their identity on an audit row (task P6-29). This replaces Core's identity-free
    // default with one that captures the signed-in editor, so item forty is still theirs.
    builder.Services.AddScoped<IBulkOperationScopeFactory, HttpBulkOperationScopeFactory>();

    // The same problem one step further removed: a scheduled job has only the user id it was written
    // with, so its caller is rebuilt from the identity tables rather than captured (task P7-13).
    builder.Services.AddSingleton<IJobIdentityScopeFactory, HttpJobIdentityScopeFactory>();

    // Claims what is due every thirty seconds. Claiming is one atomic UPDATE … OUTPUT, so running
    // this on every instance is correct rather than merely tolerated (risk R16).
    builder.Services.AddHostedService<PublishSchedulerService>();

    // Backs the structure admin screens while they pre-render, calling the services directly rather
    // than looping back through the HTTP API (task P1-29). Scoped alongside them, the gate keeps the
    // components Blazor initializes concurrently from using this request's one DbContext at once
    // (ADR-0022).
    builder.Services.AddScoped<PrerenderGate>();
    builder.Services.AddScoped<IStructureClient, ServerStructureClient>();
    builder.Services.AddScoped<INavigationClient, ServerNavigationClient>();
    builder.Services.AddScoped<ISearchClient, ServerSearchClient>();
    builder.Services.AddScoped<IPageClient, ServerPageClient>();
    builder.Services.AddScoped<IReusableClient, ServerReusableClient>();
    builder.Services.AddScoped<IMediaClient, ServerMediaClient>();
    builder.Services.AddScoped<IMarkupPreviewClient, ServerMarkupPreviewClient>();
    builder.Services.AddScoped<IWorkflowClient, ServerWorkflowClient>();
    builder.Services.AddScoped<ICurrentUserClient, ServerCurrentUserClient>();
    builder.Services.AddScoped<IDashboardClient, ServerDashboardClient>();
    builder.Services.AddScoped<IToastService, ToastService>();

    // The three content security policies of spec section 20.5, the per-request nonce the backoffice
    // one is written around, and the four headers that go out beside it (tasks P9-01, P9-02). Public
    // is the default and nothing opts into it; the two wider profiles are named on the endpoints
    // below (ADR-0026).
    builder.Services.AddCmsSecurityHeaders(builder.Configuration);

    // HSTS, configured rather than left at the framework's 30-day default (task P9-02). A year with
    // subdomains included is what the preload list asks for; submission to that list is deliberately
    // not automated, because it is an operational commitment that is hard to walk back and it is not
    // this application's to make.
    builder.Services.AddHsts(options =>
    {
        options.MaxAge = TimeSpan.FromDays(365);
        options.IncludeSubDomains = true;
    });

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

    // After routing — which WebApplication has already inserted at the head of the pipeline — so the
    // endpoint's CSP profile is visible, and before the output cache so a cached page still carries a
    // header written for this request (tasks P9-01, P9-02).
    app.UseCmsSecurityHeaders();

    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();

    // After authorization, so the limiter reads the endpoint's policy metadata. Only the shared
    // preview routes opt in; everything else is unlimited, because a limiter in front of the whole
    // site is a denial-of-service tool pointed at its own visitors.
    app.UseRateLimiter();

    // After authentication and authorization, and that ordering is the correctness rule of spec
    // section 16.4 rather than a preference: the page policy refuses to cache a request carrying an
    // identity, and it can only see one because those two middlewares have already run.
    app.UseOutputCache();

    app.MapStaticAssets();

    // The backoffice policy, and the only place it is granted (task P9-01). App.razor is the shell
    // for /admin and for the Identity account pages alike, and it is the one document that carries
    // the WebAssembly bootstrapper, the import map, and the editor bundles — the three things
    // 'wasm-unsafe-eval' and the nonce exist for. The public site is rendered by
    // CmsDeliveryDocument instead, which is why it can stay on the strict profile (ADR-0002).
    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(ContentManagementSystem.Client._Imports).Assembly)
        .WithCspProfile(CmsCspProfile.Backoffice);

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

    // sitemap.xml and robots.txt (tasks P8-04 and P8-05). Also before the catch-all, and also
    // reserved slugs, so neither can be shadowed by content.
    app.MapCmsSeo();

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