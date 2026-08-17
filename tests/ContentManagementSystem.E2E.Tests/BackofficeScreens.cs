using System.Security.Claims;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Renders a backoffice screen to HTML, for the gates that judge one (tasks P6-36, P6-38).
/// </summary>
/// <remarks>
/// Shared by the accessibility gate and the zoom pass, because the two must be looking at the same
/// screen: an audit that passed on a differently-configured render would be an audit of a page
/// nobody uses. The service graph is the real one wherever the answer matters — notably the field
/// editor catalog, since which control an author meets for a field type is exactly what is being
/// judged.
/// </remarks>
internal static class BackofficeScreens
{
    /// <summary>
    /// Renders one component to HTML with the framework's static renderer.
    /// </summary>
    /// <param name="component">The component type.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <remarks>
    /// Signed in as an Administrator, which is deliberate: these screens hide the save, publish, and
    /// restore controls behind <c>AuthorizeView</c>, and a gate run as a Viewer would inspect a page
    /// with no buttons on it and find nothing to complain about.
    /// </remarks>
    public static async Task<string> RenderAsync(Type component, Dictionary<string, object?> parameters)
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddScoped<IPageClient, FakePageClient>();
        services.AddScoped<IReusableClient, FakeReusableClient>();
        services.AddScoped<IMediaClient, FakeMediaClient>();

        // The field editor catalog and the preview pipeline the editors reach through
        // (tasks P6-06 to P6-15). The catalog is the real one: which control an author meets for a
        // field type is exactly what these gates should be judging, and stubbing it would have them
        // inspect a screen nobody uses.
        services.AddSingleton<IFieldEditorCatalog>(new FieldEditorCatalog());
        services.AddScoped<IMarkupPreviewClient, FakeMarkupPreviewClient>();
        services.AddSingleton(TimeProvider.System);

        // The three the editor gained with autosave, the properties panel, and the dashboard
        // (tasks P6-17, P6-18, P6-21, P6-24): who is signed in, where a completed write says so, and
        // what the landing screen shows.
        services.AddScoped<ICurrentUserClient, FakeCurrentUserClient>();
        services.AddScoped<IDashboardClient, FakeDashboardClient>();
        services.AddScoped<IToastService, SilentToastService>();

        // Two hosting services a real pre-render supplies and a bare collection does not: the
        // uploader's <InputFile> resolves IJSRuntime on construction, and the media item screen
        // navigates away after a permanent delete. Neither runs during a static render.
        services.AddScoped<NavigationManager, StaticNavigationManager>();
        services.AddScoped<IJSRuntime, UnavailableJSRuntime>();
        services.AddAuthorizationCore();
        services.AddCascadingAuthenticationState();
        services.AddScoped<AuthenticationStateProvider, AdministratorStateProvider>();

        await using var provider = services.BuildServiceProvider();
        await using var renderer = new PrerenderingHtmlRenderer(
            provider,
            provider.GetRequiredService<ILoggerFactory>());

        return await renderer.RenderAsync(component, ParameterView.FromDictionary(parameters));
    }

    /// <summary>Signs the render in as an Administrator, so every gated control is on the page.</summary>
    private sealed class AdministratorStateProvider : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "1"),
                    new Claim(ClaimTypes.Name, "test-editor"),
                    new Claim(ClaimTypes.Role, CmsRoles.Administrator),
                ],
                authenticationType: "Test",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);

            return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
    }
}
