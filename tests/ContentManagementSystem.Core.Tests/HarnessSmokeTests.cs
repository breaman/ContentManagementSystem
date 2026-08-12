using Bunit;

using ContentManagementSystem.Core;
using ContentManagementSystem.Rendering;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Core.Tests;

/// <summary>
/// Proves the unit-test harness itself works: xUnit discovery, FluentAssertions, and bUnit
/// rendering (tasks P0-08, P0-12).
/// </summary>
/// <remarks>
/// These assertions are deliberately trivial. Their job is to fail loudly if the harness breaks,
/// so that a genuine test failure later is never mistaken for a tooling problem.
/// </remarks>
public class HarnessSmokeTests
{
    [Fact]
    public void CoreAndRenderingAssembliesAreResolvable()
    {
        CoreAssemblyMarker.Assembly.GetName().Name.Should().Be("ContentManagementSystem.Core");
        RenderingAssemblyMarker.Assembly.GetName().Name.Should().Be("ContentManagementSystem.Rendering");
    }

    [Fact]
    public void BunitCanRenderAComponent()
    {
        using var context = new BunitContext();

        var rendered = context.Render(builder =>
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, "harness");
            builder.CloseElement();
        });

        rendered.Markup.Should().Be("<p>harness</p>");
    }
}
