using System.Diagnostics;

using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Common;
using ContentManagementSystem.Data.Interfaces;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Server.Authorization;
using ContentManagementSystem.Server.Cli;
using ContentManagementSystem.Server.HealthChecks;
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

    builder.Services.Configure<SchemaSyncOptions>(
        builder.Configuration.GetSection(SchemaSyncOptions.SectionName));

    // The cms-templates check of spec section 24.2. Degraded, never unhealthy: a bad deployment must
    // be visible without taking down a site whose pages still render.
    builder.Services.AddHealthChecks()
        .AddCheck<CmsTemplatesHealthCheck>(
            CmsTemplatesHealthCheck.Name,
            tags: ["ready", "cms"]);

    // The management API is cookie-authenticated, so every write carries an antiforgery token in a
    // header. Naming the header here is what lets the JSON endpoints validate one at all — the
    // default configuration only reads the token from a form field.
    builder.Services.AddAntiforgery(options => options.HeaderName = CmsAntiforgeryDefaults.HeaderName);

    // Runs the reconciliation and then the schema sync, in that order, once at startup.
    builder.Services.AddHostedService<CmsStructureStartupService>();

    builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();
    builder.Services.AddScoped<IUserService, HttpUserService>();

    // Backs the structure admin screens while they pre-render, calling the services directly rather
    // than looping back through the HTTP API (task P1-29).
    builder.Services.AddScoped<IStructureClient, ServerStructureClient>();
    builder.Services.AddScoped<IPageClient, ServerPageClient>();
    builder.Services.AddScoped<IToastService, ToastService>();

    // Add route configuration to enforce lowercase URLs for better SEO
    builder.Services.Configure<RouteOptions>(options =>
    {
        options.LowercaseUrls = true;
        options.LowercaseQueryStrings = true;
        options.AppendTrailingSlash = false;
    });

    var app = builder.Build();

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
    app.UseWhen(
        context => !context.Request.Path.StartsWithSegments(CmsApiEndpoints.ApiPathPrefix),
        branch => branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));

    app.UseHttpsRedirection();
    app.UseAuthentication();
    app.UseAuthorization();
    app.UseAntiforgery();
    app.MapStaticAssets();

    app.MapRazorComponents<App>()
        .AddInteractiveWebAssemblyRenderMode()
        .AddAdditionalAssemblies(typeof(ContentManagementSystem.Client._Imports).Assembly);

    app.MapAdditionalIdentityEndpoints();
    app.MapCmsApi();

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