using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddScoped(sp =>
    new HttpClient
    {
        BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
    });

builder.Services.AddScoped<IToastService, ToastService>();

// The structure admin screens talk to the management API from the browser. Its server-side twin,
// ServerStructureClient, backs the same screens during pre-render.
builder.Services.AddScoped<IStructureClient, HttpStructureClient>();

// The page admin screens, likewise, with ServerPageClient behind the same interface on the server.
builder.Services.AddScoped<IPageClient, HttpPageClient>();

// The reusable content library, and its server twin ServerReusableClient.
builder.Services.AddScoped<IReusableClient, HttpReusableClient>();

await builder.Build().RunAsync();