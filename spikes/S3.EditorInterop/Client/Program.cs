using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using S3.EditorInterop.Client.Pages;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// No router: the spike has exactly one screen, mounted straight into the host page.
builder.RootComponents.Add<EditorHarness>("#app");

await builder.Build().RunAsync();
