using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    });

// The clock the backoffice screens read. The server registers the same instance through
// AddCmsPages, so a component that asks "is this schedule in the future" gets an answer of the same
// kind whether it is pre-rendering or running in the browser.
builder.Services.TryAddSingleton(TimeProvider.System);

builder.Services.AddScoped<IToastService, ToastService>();

// The backoffice shell's pane geometry, kept in localStorage (task P6-01). Registered on the server
// too, where every call is a no-op, so the shell pre-renders with the default layout.
builder.Services.AddScoped<IShellLayoutStore, BrowserShellLayoutStore>();

// The structure admin screens talk to the management API from the browser. Its server-side twin,
// ServerStructureClient, backs the same screens during pre-render.
builder.Services.AddScoped<IStructureClient, HttpStructureClient>();

// The page admin screens, likewise, with ServerPageClient behind the same interface on the server.
builder.Services.AddScoped<IPageClient, HttpPageClient>();

// The reusable content library, and its server twin ServerReusableClient.
builder.Services.AddScoped<IReusableClient, HttpReusableClient>();

// The media library, the picker, and the resumable uploader; ServerMediaClient is its twin.
builder.Services.AddScoped<IMediaClient, HttpMediaClient>();

await builder.Build().RunAsync();