using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields;

/// <summary>
/// Covers the registry's startup guarantees (task P1-09).
/// </summary>
public class FieldTypeRegistryTests
{
    [Fact]
    public void FindReturnsTheFieldTypeRegisteredUnderAKey()
    {
        var registry = new FieldTypeRegistry([new StubFieldType("richText"), new StubFieldType("plainText")]);

        registry.Find("richText")!.Key.Should().Be("richText");
    }

    [Fact]
    public void FindReturnsNullForAnUnknownKey()
    {
        var registry = new FieldTypeRegistry([new StubFieldType("richText")]);

        // Content outlives the deployment that wrote it, so a payload naming a field type nobody
        // registers any more is expected. Delivery logs and renders nothing; it never throws.
        registry.Find("mysteryType").Should().BeNull();
        registry.Contains("mysteryType").Should().BeFalse();
    }

    [Fact]
    public void KeyLookupIsCaseSensitive()
    {
        var registry = new FieldTypeRegistry([new StubFieldType("richText")]);

        // Keys are matched byte-for-byte against stored payloads; treating "RichText" as a hit
        // would let two spellings of one key drift apart in stored content.
        registry.Find("richtext").Should().BeNull();
    }

    [Fact]
    public void TwoFieldTypesSharingAKeyFailAtStartup()
    {
        var construct = () => new FieldTypeRegistry([new StubFieldType("richText"), new StubFieldType("richText")]);

        construct.Should().Throw<InvalidOperationException>()
            .WithMessage("*richText*");
    }

    [Fact]
    public void AFieldTypeWithNoKeyFailsAtStartup()
    {
        var construct = () => new FieldTypeRegistry([new StubFieldType("  ")]);

        construct.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AllIsOrderedByKey()
    {
        var registry = new FieldTypeRegistry(
            [new StubFieldType("richText"), new StubFieldType("boolean"), new StubFieldType("media")]);

        registry.All.Select(f => f.Key).Should().Equal("boolean", "media", "richText");
    }

    [Fact]
    public void AnEmptyRegistryIsUsable()
    {
        var registry = new FieldTypeRegistry([]);

        registry.All.Should().BeEmpty();
        registry.Find("richText").Should().BeNull();
    }

    private sealed class StubFieldType(string key) : IFieldType
    {
        public string Key { get; } = key;

        public string DisplayName => Key;

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
    }
}
