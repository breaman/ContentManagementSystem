using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Rendering;
using ContentManagementSystem.Rendering.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Core.Tests.Rendering;

/// <summary>
/// The field type to renderer mapping and the startup check over it (task P3-09, ADR-0014).
/// </summary>
public class FieldRendererCatalogTests
{
    [Fact]
    public void EveryFieldTypeARealDeploymentRegistersResolvesToARenderer()
    {
        // The acceptance the task exists for. Adding a field type without a renderer fails here, by
        // name, rather than showing up as a missing paragraph on a public page months later.
        var catalog = new FieldRendererCatalog(FieldRendererHarness.Registry);

        catalog.FieldTypesWithNoRenderer.Should().BeEmpty();
        catalog.FieldTypeKeys.Should().HaveCount(FieldRendererHarness.Registry.All.Count);
    }

    [Fact]
    public void TheCatalogDescribesTheDeploymentRatherThanTheBuiltInTable()
    {
        // A field type that was removed from the deployment must have no renderer, or content
        // authored against it would keep rendering after the decision to retire it.
        var catalog = FieldRendererCatalog.For(new StubFieldType("plainText"));

        catalog.FieldTypeKeys.Should().ContainSingle().Which.Should().Be("plainText");
        catalog.TryGetRenderer("richText", out _).Should().BeFalse();
    }

    [Fact]
    public void AFieldTypeThatNamesItsOwnRendererIsTakenAtItsWord()
    {
        // The extension author's path: a field type shipped above Rendering in the reference graph
        // can name a component directly and needs no entry in the built-in table (ADR-0014).
        var catalog = FieldRendererCatalog.For(
            new StubFieldType("plainText", typeof(AlternateFieldRenderer)));

        catalog.TryGetRenderer("plainText", out var renderer).Should().BeTrue();
        renderer.Should().Be<AlternateFieldRenderer>();
    }

    [Fact]
    public void AFieldTypeWithNoRendererFromEitherSourceIsReportedRatherThanThrown()
    {
        // Reported, because the pages using it still render everything else and every other page on
        // the site is fine. Silent is the thing that is not acceptable.
        var catalog = FieldRendererCatalog.For(new StubFieldType("nobodysFieldType"));

        catalog.FieldTypesWithNoRenderer.Should().ContainSingle()
            .Which.Should().Be("nobodysFieldType");
        catalog.TryGetRenderer("nobodysFieldType", out _).Should().BeFalse();
    }

    [Fact]
    public void ARendererThatIsNotAComponentIsRefusedAtStartup()
    {
        // Same rule CmsComponentCatalog applies to a template declaration on a non-component:
        // rendering it would fail one page at a time, in production.
        var build = () => FieldRendererCatalog.For(new StubFieldType("odd", typeof(FieldRendererCatalogTests)));

        build.Should().Throw<InvalidOperationException>()
            .WithMessage("*is not a Razor component*");
    }

    [Fact]
    public void AnUnknownKeyResolvesToNothingRatherThanThrowing()
    {
        // The key comes out of a stored payload, where anything is possible and nothing may throw on
        // the delivery path (spec section 15.3).
        var catalog = new FieldRendererCatalog(FieldRendererHarness.Registry);

        catalog.TryGetRenderer("retiredFieldType", out _).Should().BeFalse();
        catalog.TryGetRenderer(string.Empty, out _).Should().BeFalse();
    }

    [Fact]
    public void EveryBuiltInRendererIsARazorComponent()
    {
        BuiltInFieldRenderers.ByFieldTypeKey.Values.Should()
            .AllSatisfy(renderer => typeof(IComponent).IsAssignableFrom(renderer).Should().BeTrue());
    }

    /// <summary>A field type that exists only to say what its key and renderer are.</summary>
    private sealed class StubFieldType(string key, Type? renderer = null) : IFieldType
    {
        public string Key => key;

        public string DisplayName => key;

        public Type? EditorComponent => null;

        public Type? RendererComponent => renderer;

        public FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

        public FieldConfigurationSchema ConfigurationSchema => FieldConfigurationSchema.Empty;

        public ValueTask<ValidationResult> ValidateAsync(
            JsonElement value,
            FieldConfiguration configuration,
            ValidationMode mode,
            CancellationToken cancellationToken) => ValueTask.FromResult(ValidationResult.Success);

        public ValueTask<JsonElement> SanitizeAsync(
            JsonElement value,
            FieldConfiguration configuration,
            CancellationToken cancellationToken) => ValueTask.FromResult(value);

        public IEnumerable<ContentReference> ExtractReferences(JsonElement value) => [];

        public string ExtractSearchText(JsonElement value) => string.Empty;

        public JsonNode? RemapReferences(JsonElement value, ReferenceRemapper remap) => null;
    }
}
