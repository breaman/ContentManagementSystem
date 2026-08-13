using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields;

/// <summary>
/// Covers parsing of a zone's stored <c>ConfigurationJson</c> (task P1-08).
/// </summary>
public class FieldConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingConfigurationParsesToEmpty(string? configurationJson)
    {
        var configuration = FieldConfiguration.Parse(configurationJson);

        configuration.IsEmpty.Should().BeTrue();
        configuration.GetInt32("maxLength").Should().BeNull();
        configuration.GetStringArray("allowedBlockTypes").Should().BeEmpty();
    }

    [Fact]
    public void TypedAccessorsReadTheirSettings()
    {
        var configuration = FieldConfiguration.Parse(
            """
            {
              "maxLength": 120,
              "step": 0.5,
              "pattern": "^[a-z]+$",
              "allowNesting": true,
              "allowedBlockTypes": ["hero-banner", "quote"]
            }
            """);

        configuration.GetInt32("maxLength").Should().Be(120);
        configuration.GetDecimal("step").Should().Be(0.5m);
        configuration.GetString("pattern").Should().Be("^[a-z]+$");
        configuration.GetBoolean("allowNesting").Should().BeTrue();
        configuration.GetStringArray("allowedBlockTypes").Should().Equal("hero-banner", "quote");
    }

    [Fact]
    public void AnAbsentSettingFallsBackRatherThanThrowing()
    {
        var configuration = FieldConfiguration.Parse("""{"maxLength": 120}""");

        configuration.GetInt32("min").Should().BeNull();
        configuration.GetString("pattern").Should().BeNull();
        configuration.GetBoolean("allowNesting").Should().BeFalse();
        configuration.GetBoolean("allowNesting", defaultValue: true).Should().BeTrue();
    }

    [Fact]
    public void ASettingOfTheWrongKindIsTreatedAsAbsent()
    {
        // Configuration is validated against a per-field-type JSON Schema on save (P1-12), so a
        // mistyped setting should not reach here. When one does, a field type falling back to its
        // default beats it throwing halfway through rendering a published page.
        var configuration = FieldConfiguration.Parse("""{"maxLength": "lots", "allowedBlockTypes": 7}""");

        configuration.GetInt32("maxLength").Should().BeNull();
        configuration.GetStringArray("allowedBlockTypes").Should().BeEmpty();
    }

    [Fact]
    public void AnExplicitNullIsTreatedAsAbsent()
    {
        var configuration = FieldConfiguration.Parse("""{"maxLength": null}""");

        configuration.TryGetValue("maxLength", out _).Should().BeFalse();
        configuration.GetInt32("maxLength").Should().BeNull();
    }

    [Fact]
    public void NonStringEntriesAreSkippedWhenReadingAStringArray()
    {
        var configuration = FieldConfiguration.Parse("""{"options": ["a", 3, null, "b"]}""");

        configuration.GetStringArray("options").Should().Equal("a", "b");
    }

    [Fact]
    public void TheParsedConfigurationOutlivesTheDocumentItCameFrom()
    {
        // The instance is cached per schema row and reused across every payload validated against
        // it, so it must not hold a disposed JsonDocument. Re-parsing per property visit dominated
        // every other cost in the S1 spike.
        var configuration = FieldConfiguration.Parse("""{"maxLength": 120}""");

        GC.Collect();

        configuration.Root.ValueKind.Should().Be(JsonValueKind.Object);
        configuration.GetInt32("maxLength").Should().Be(120);
    }

    [Fact]
    public void MalformedConfigurationThrows()
    {
        var parse = () => FieldConfiguration.Parse("{ not json");

        parse.Should().Throw<JsonException>();
    }
}
