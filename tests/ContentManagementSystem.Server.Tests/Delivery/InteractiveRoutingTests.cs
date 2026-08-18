using System.Reflection;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// Interactivity is scoped to the backoffice (task P3-14, spec section 5.3).
/// </summary>
/// <remarks>
/// The decision output caching rests on, turned into something a build enforces rather than
/// something a reviewer notices. A page that declares a render mode downloads the WebAssembly
/// runtime to whoever opens it and cannot be served from a cache; put one in the public route space
/// and the whole reason the two front doors are separate is gone, silently, for that URL.
/// <para>
/// Reflection over the routable components rather than a search of the <c>.razor</c> files:
/// <c>@rendermode</c> compiles to a <see cref="RenderModeAttribute"/> on the generated class, so this
/// sees what the compiler saw, including any component that acquires one from a base class or a
/// source generator.
/// </para>
/// </remarks>
public class InteractiveRoutingTests
{
    [Test]
    public void EveryRoutableComponentWithARenderModeLivesUnderAdmin()
    {
        var offenders = RoutableComponents()
            .Where(component => component.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null)
            .SelectMany(Routes)
            .Where(route => !route.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
            .ToList();

        offenders.Should().BeEmpty(
            "a routable component carrying a render mode is an interactive page, and an interactive " +
            "page outside /admin is a public URL that cannot be output cached");
    }

    [Test]
    public void TheScanFindsTheBackofficePagesItIsSupposedToBeChecking()
    {
        // A test that asserts an empty set passes just as well when the scan finds nothing at all —
        // a namespace rename, a moved assembly, a changed attribute name. This is the tripwire for
        // that: the backoffice pages exist and are interactive, so the scan must see them.
        var interactive = RoutableComponents()
            .Where(component => component.GetCustomAttribute<RenderModeAttribute>(inherit: true) is not null)
            .ToList();

        interactive.Should().HaveCountGreaterThan(3);
    }

    private static IEnumerable<Type> RoutableComponents() =>
        new[]
        {
            typeof(Program).Assembly,
            typeof(Client._Imports).Assembly,
        }
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => typeof(IComponent).IsAssignableFrom(type) &&
            type.GetCustomAttributes<RouteAttribute>(inherit: true).Any());

    private static IEnumerable<string> Routes(Type component) =>
        component.GetCustomAttributes<RouteAttribute>(inherit: true).Select(route => route.Template);
}
