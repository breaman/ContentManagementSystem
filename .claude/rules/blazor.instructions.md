---
description: 'Blazor component and application patterns'
paths: ['**/*.razor', '**/*.razor.cs', '**/*.razor.css']
---

## Blazor Code Style and Structure

- Write idiomatic and efficient Blazor and C# code.
- Follow .NET and Blazor conventions.
- Use Razor Components appropriately for component-based UI development.
- Always use code-behind files or service classes for code in a Blazor file, there should never be a "code" block inside the Razor page.
- Async/await should be used where applicable to ensure non-blocking UI operations.

## UI Conventions

- When needing css for a component, favor using the bootstrap css framework and only use component-specific css when necessary, and place it in the same folder as the component with a .razor.css extension.
- When utilizing icons, use the bootstrap icons library and ensure that icons are used consistently across the application for a cohesive user experience.
- All new form input controls must be wrapped in a Bootstrap `form-floating` div.
Example structure:
```html
<div class="form-floating mb-3">
<InputText @bind-Value="Model.Property" id="Model.Property" class="form-control" placeholder="..." />
<label for="Model.Property">Label Text</label>
<ValidationMessage For="() => Model.Property" class="text-danger" />
</div>
```

## Naming Conventions

- Follow PascalCase for component names, method names, and public members.
- Use camelCase for private fields and local variables.
- Prefix interface names with "I" (e.g., IUserService).

## Blazor and .NET Specific Guidelines

- Utilize Blazor's built-in features for component lifecycle (e.g., OnInitializedAsync, OnParametersSetAsync).
- Use data binding effectively with @bind.
- Leverage Dependency Injection for services in Blazor.
- Structure Blazor components and services following Separation of Concerns.
- Always use the latest version C#, currently C# 14 features like record types, pattern matching, and global usings.

## Error Handling and Validation

- Implement proper error handling for Blazor pages and API calls.
- Use logging for error tracking in the backend and consider capturing UI-level errors in Blazor with tools like ErrorBoundary.
- Implement validation using FluentValidation or DataAnnotations in forms.

## Blazor API and Performance Optimization

- Utilize Blazor server-side or WebAssembly optimally based on the project requirements.
- Use asynchronous methods (async/await) for API calls or UI actions that could block the main thread.
- Optimize Razor components by reducing unnecessary renders and using StateHasChanged() efficiently.
- Minimize the component render tree by avoiding re-renders unless necessary, using ShouldRender() where appropriate.
- Use EventCallbacks for handling user interactions efficiently, passing only minimal data when triggering events.

## Caching Strategies

- Implement in-memory caching for frequently used data, especially for Blazor Server apps. Use IMemoryCache for lightweight caching solutions.
- For Blazor WebAssembly, utilize localStorage or sessionStorage to cache application state between user sessions.
- Consider Distributed Cache strategies (like Redis or SQL Server Cache) for larger applications that need shared state across multiple users or clients.
- Cache API calls by storing responses to avoid redundant calls when data is unlikely to change, thus improving the user experience.

## State Management Libraries

- Use Blazor's built-in Cascading Parameters and EventCallbacks for basic state sharing across components.
- Implement advanced state management solutions using libraries like Fluxor or BlazorState when the application grows in complexity.
- For client-side state persistence in Blazor WebAssembly, consider using Blazored.LocalStorage or Blazored.SessionStorage to maintain state between page reloads.
- For server-side Blazor, use Scoped Services and the StateContainer pattern to manage state within user sessions while minimizing re-renders.

## Render Mode Policy

**Never use `@rendermode InteractiveServer` or `InteractiveServerRenderMode`.** All interactive components must use `@rendermode InteractiveWebAssembly` exclusively. InteractiveServer uses a SignalR circuit that consumes server resources per connected user and does not align with this project's WebAssembly-first architecture.

## InteractiveWebAssembly Pre-Rendering Pattern

When a component uses `@rendermode InteractiveWebAssembly`, it must support pre-rendering so the user sees content immediately while the WebAssembly runtime downloads. Follow the dual-mode service pattern demonstrated by the Weather page. Any component that uses `@rendermode InteractiveWebAssembly` must also live in the Client project.

### Shared Service Contract

Define the service interface in the Shared project so both client and server can implement it:

```csharp
public interface IWeatherService
{
    Task<WeatherForecast[]> GetWeatherForecastsAsync();
}
```

### Dual Implementation

Register the same interface with different implementations on each platform.

**Client implementation** (`Client` project — calls the HTTP API):
```csharp
public class ClientWeatherService(HttpClient http) : IWeatherService
{
    public async Task<WeatherForecast[]> GetWeatherForecastsAsync()
    {
        return await http.GetFromJsonAsync<WeatherForecast[]>("api/weather") ?? [];
    }
}
```

**Server implementation** (`Server` project — calls the database or data service directly):
```csharp
public class ServerWeatherService(WeatherDataService weatherData) : IWeatherService
{
    public Task<WeatherForecast[]> GetWeatherForecastsAsync()
    {
        return Task.FromResult(weatherData.GetForecasts());
    }
}
```

**Registration**:
- In `Client/Program.cs`: `builder.Services.AddScoped<IWeatherService, ClientWeatherService>();`
- In `Server/Program.cs`: `builder.Services.AddScoped<IWeatherService, ServerWeatherService>();`

### Component State Persistence

In the component code-behind, decorate properties that hold pre-rendered data with `[PersistentState]`. During server pre-rendering these properties are populated and serialized into the page. When WebAssembly hydrates the component, the state is restored automatically.

```csharp
public partial class Weather : ComponentBase
{
    [Inject] private IWeatherService WeatherService { get; set; } = default!;

    [PersistentState]
    public WeatherForecast[]? Forecasts { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Forecasts ??= await WeatherService.GetWeatherForecastsAsync();
    }
}
```

Key rules:
- Use `??=` in `OnInitializedAsync` so data is fetched only when persistent state was not restored.
- The `[PersistentState]` attribute must be applied to public properties that hold the pre-rendered data.
- The component should handle `null` state gracefully in the Razor markup (e.g., show a loading indicator).

## API Design and Integration

- Use HttpClient or other appropriate services to communicate with external APIs or your own backend.
- Implement error handling for API calls using try-catch and provide proper user feedback in the UI.

## Testing and Debugging in Visual Studio

- All unit testing and integration testing should be done in Visual Studio Enterprise.
- Test Blazor components and services with TUnit and bUnit — TUnit is this repository's test framework, and no test should introduce another.
- Use Moq or NSubstitute for mocking dependencies during tests.
- Debug Blazor UI issues using browser developer tools and Visual Studio's debugging tools for backend and server-side issues.
- For performance profiling and optimization, rely on Visual Studio's diagnostics tools.

## Security and Authentication

- Implement Authentication and Authorization in the Blazor app where necessary using ASP.NET Identity or JWT tokens for API authentication.
- Use HTTPS for all web communication and ensure proper CORS policies are implemented.

## API Documentation and Swagger

- Use Swagger/OpenAPI for API documentation for your backend API services.
- Ensure XML documentation for models and API methods for enhancing Swagger documentation.

## Troubleshooting: RZ1021 build errors

If a build fails with **RZ1021** ("Markup in a code block must start with a tag and all start tags
must be matched with end tags"), this is a known issue with .NET SDK 10.0.301 and **not** a defect
in the Razor markup. Run `dotnet build-server shutdown`, then build again.

A poisoned Razor compilation server misparses component tags inside code blocks
(`@if { <SomeComponent /> }`); everything after the first bad tag is then parsed as C#, producing
hundreds of bogus CS errors in untouched files. It reproduces on a brand-new `dotnet new blazor`
project, so it is not caused by this repository's code.

Shut down the build server first. Do not edit the Razor files, clean `obj/`/`bin/`, or change the
SDK pin in `global.json` — those are dead ends.