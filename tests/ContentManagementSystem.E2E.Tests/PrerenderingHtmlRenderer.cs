using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// A static renderer that will render components declaring a render mode.
/// </summary>
/// <param name="services">Services the components resolve from.</param>
/// <param name="loggerFactory">Log for the renderer itself.</param>
/// <remarks>
/// Resolving a render mode is the hosting layer's job, and a renderer with no hosting layer refuses
/// any component carrying <c>@rendermode</c> — which is every screen in the backoffice. The server's
/// own endpoint renderer overrides the same method to choose between pre-rendering and emitting a
/// hydration marker.
/// <para>
/// Here every render mode resolves to "render it statically", which produces the markup the server
/// emits while pre-rendering: the first thing a user sees, before the WebAssembly runtime has
/// finished downloading. That is the right markup for an accessibility gate to judge — a screen that
/// only becomes accessible after hydration is not accessible.
/// </para>
/// <para>
/// Built on <see cref="StaticHtmlRenderer"/> rather than <c>HtmlRenderer</c>, which is sealed.
/// </para>
/// </remarks>
public sealed class PrerenderingHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
    : StaticHtmlRenderer(services, loggerFactory)
{
    /// <summary>
    /// Renders one component to HTML, waiting for its asynchronous work to settle.
    /// </summary>
    /// <param name="componentType">The component to render.</param>
    /// <param name="parameters">Its parameters.</param>
    /// <returns>The markup the component produces.</returns>
    /// <remarks>
    /// Awaiting quiescence is what makes the output worth checking: these screens load their content
    /// in <c>OnInitializedAsync</c>, so without it the gate would inspect a page reading
    /// "Loading templates…" and report that it is perfectly accessible.
    /// </remarks>
    public async Task<string> RenderAsync(Type componentType, ParameterView parameters)
    {
        return await Dispatcher.InvokeAsync(async () =>
        {
            var component = BeginRenderingComponent(componentType, parameters);

            await component.QuiescenceTask;

            await using var writer = new StringWriter();

            component.WriteHtmlTo(writer);

            return writer.ToString();
        });
    }

    /// <inheritdoc />
    protected override IComponent ResolveComponentForRenderMode(
        Type componentType,
        int? parentComponentId,
        IComponentActivator componentActivator,
        IComponentRenderMode renderMode) =>
        componentActivator.CreateInstance(componentType);
}
