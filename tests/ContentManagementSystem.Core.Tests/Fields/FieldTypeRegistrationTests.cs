using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Fields;

/// <summary>
/// Covers the documented extension point for adding a field type (task P1-09, spec section 7.3).
/// </summary>
public class FieldTypeRegistrationTests
{
    [Test]
    public void AddCmsFieldTypeMakesTheFieldTypeResolvable()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldType<SampleFieldType>()
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        registry.Find("sample").Should().BeOfType<SampleFieldType>();
    }

    [Test]
    public void RegisteringTheSameFieldTypeTwiceDoesNotTripTheDuplicateKeyGuard()
    {
        // The built-in scan and an explicit registration can name the same type. That is a
        // configuration accident, not two field types fighting over a key, so it must not take the
        // application down at startup.
        using var provider = new ServiceCollection()
            .AddCmsFieldType<SampleFieldType>()
            .AddCmsFieldType<SampleFieldType>()
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        registry.All.Should().ContainSingle();
    }

    [Test]
    public void FieldTypesAreSingletons()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldType<SampleFieldType>()
            .BuildServiceProvider();

        var first = provider.GetRequiredService<IFieldTypeRegistry>();
        var second = provider.GetRequiredService<IFieldTypeRegistry>();

        // Implementations are documented as stateless and thread-safe precisely because one
        // instance serves every concurrent save and publish.
        first.Should().BeSameAs(second);
        first.Find("sample").Should().BeSameAs(second.Find("sample"));
    }

    [Test]
    public void ScanningAnAssemblyPicksUpItsPublicFieldTypes()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldTypesFrom(typeof(FieldTypeRegistrationTests).Assembly)
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        registry.Find("discoverable").Should().BeOfType<DiscoverableFieldType>();
    }

    [Test]
    public void ScanningSkipsNonPublicImplementations()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldTypesFrom(typeof(FieldTypeRegistrationTests).Assembly)
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        // A field type's key ends up in stored payloads, so registration is a deliberate act. The
        // private stubs in this assembly are test doubles — one of them cannot even be constructed
        // by the container — and picking them up would fail startup on an assembly's private
        // details.
        registry.Find("sample").Should().BeNull();
        registry.All.Should().ContainSingle();
    }

    private sealed class SampleFieldType : IFieldType
    {
        public string Key => "sample";

        public string DisplayName => "Sample";

        public Type EditorComponent => typeof(object);

        public Type RendererComponent => typeof(object);

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
