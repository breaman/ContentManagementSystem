using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Configuration;
using ContentManagementSystem.Core.Tests.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Fields.Configuration;

/// <summary>
/// Checking a zone's <c>ConfigurationJson</c> against its field type before it is stored (task
/// P1-12, spec section 7.2).
/// </summary>
/// <remarks>
/// Driven against the field types a real deployment registers rather than a stub schema, because
/// the failure worth catching is a real zone configured with a setting its real field type does not
/// read.
/// </remarks>
public class FieldConfigurationValidatorTests
{
    private readonly IFieldConfigurationValidator _validator = Validator();

    [Fact]
    public void NoConfigurationIsValid()
    {
        _validator.Validate(FieldTypeKeys.PlainText, null).IsValid.Should().BeTrue();
        _validator.Validate(FieldTypeKeys.PlainText, "   ").IsValid.Should().BeTrue();
    }

    [Fact]
    public void AFieldTypeThatIsNotRegisteredIsRefused()
    {
        var result = _validator.Validate("markdownTable", """{ "maxLength": 10 }""");

        // Unlike delivery, which logs and renders nothing: nothing is stored yet, and binding a
        // zone to a field type no deployment has is a structure the editor could never render.
        result.Codes().Should().Equal(FieldConfigurationCodes.UnknownFieldType);
    }

    [Fact]
    public void MalformedJsonIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, "{ not json");

        result.Codes().Should().Equal(FieldConfigurationCodes.Malformed);
    }

    [Fact]
    public void ConfigurationThatIsNotAnObjectIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """[ "maxLength" ]""");

        result.Codes().Should().Equal(FieldConfigurationCodes.Shape);
    }

    [Fact]
    public void AValidConfigurationPassesWithNothingReported()
    {
        var result = _validator.Validate(
            FieldTypeKeys.PlainText,
            """{ "minLength": 2, "maxLength": 60, "pattern": "^[A-Z]", "patternMessage": "Start with a capital." }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ASettingTheFieldTypeDoesNotDeclareIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "allowedBlockTypes": ["hero"] }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.UnknownSetting);
        result.Paths().Should().Equal("allowedBlockTypes");
    }

    [Fact]
    public void AMistypedSettingNamesTheOneThatWasMeant()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "maxlength": 60 }""");

        // The whole point of a closed schema: without this, a mistyped setting persists happily and
        // silently does nothing.
        result.Codes().Should().Equal(FieldConfigurationCodes.UnknownSetting);
        result.Diagnostics[0].Message.Should().Contain("maxLength");
    }

    [Fact]
    public void AFieldTypeTakingNoConfigurationSaysSo()
    {
        var result = _validator.Validate(FieldTypeKeys.Boolean, """{ "maxLength": 4 }""");

        result.Diagnostics[0].Message.Should().Contain("takes no configuration");
    }

    [Fact]
    public void RequiredBelongsToTheZoneNotTheConfiguration()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "required": true }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.RequiredReserved);
    }

    [Fact]
    public void ASettingOfTheWrongTypeIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "maxLength": "sixty" }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.SettingType);
    }

    [Fact]
    public void AFractionalWholeNumberIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "maxLength": 60.5 }""");

        // GetInt32 would return null for it and the setting would be silently ignored.
        result.Codes().Should().Equal(FieldConfigurationCodes.SettingType);
    }

    [Fact]
    public void ANullSettingReadsAsAnAbsentOne()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "maxLength": null }""");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ASettingBelowItsMinimumIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "maxLength": 0 }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.SettingRange);
    }

    [Fact]
    public void AStepOfZeroIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.Number, """{ "step": 0 }""");

        // The field type applies a step only when it is positive, so zero is a setting that reads
        // as configured and does nothing.
        result.Codes().Should().Equal(FieldConfigurationCodes.SettingRange);
    }

    [Fact]
    public void APatternThatDoesNotCompileIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.PlainText, """{ "pattern": "([a-z" }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.SettingFormat);
    }

    [Fact]
    public void ABoundInTheWrongDateSyntaxIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.Date, """{ "min": "13/08/2026" }""");

        // Refused with the same parser the field type will use to read the bound, so a configuration
        // accepted here is one that is actually enforced later.
        result.Codes().Should().Equal(FieldConfigurationCodes.SettingFormat);
    }

    [Fact]
    public void AnInstantWithNoOffsetIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.DateTime, """{ "max": "2026-08-13T09:30:00" }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.SettingFormat);
    }

    [Fact]
    public void APaletteEntryThatIsNotAHexColourIsRefusedByPosition()
    {
        var result = _validator.Validate(
            FieldTypeKeys.Color,
            """{ "palette": ["#1f6feb", "rebeccapurple"] }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.SettingFormat);
        result.Paths().Should().Equal("palette[1]");
    }

    [Fact]
    public void AProfileOutsideTheClosedSetIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.RichText, """{ "profile": "developer" }""");

        // Reachable only from the html field type, which carries the role gate that justifies it.
        result.Codes().Should().Equal(FieldConfigurationCodes.SettingValue);
    }

    [Fact]
    public void AMinimumAboveItsMaximumIsRefused()
    {
        var result = _validator.Validate(FieldTypeKeys.Blocks, """{ "min": 5, "max": 2 }""");

        // No value can satisfy both, and without this the contradiction surfaces as an editor who
        // cannot publish and cannot see why.
        result.Codes().Should().Equal(FieldConfigurationCodes.RangeInverted);
    }

    [Fact]
    public void EqualBoundsAreAllowed()
    {
        _validator.Validate(FieldTypeKeys.Blocks, """{ "min": 3, "max": 3 }""").IsValid.Should().BeTrue();
    }

    [Fact]
    public void AnInvertedDateRangeIsRefused()
    {
        var result = _validator.Validate(
            FieldTypeKeys.Date,
            """{ "min": "2026-08-13", "max": "2026-01-01" }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.RangeInverted);
    }

    [Fact]
    public void ARangeIsNotReportedTwiceWhenABoundIsAlreadyInvalid()
    {
        var result = _validator.Validate(FieldTypeKeys.Blocks, """{ "min": "five", "max": 2 }""");

        result.Codes().Should().Equal(FieldConfigurationCodes.SettingType);
    }

    [Fact]
    public void ASettingWhoseEnforcingPhaseHasNotShippedIsStoredWithAWarning()
    {
        var result = _validator.Validate(
            DeferredSettingFieldType.TypeKey,
            """{ "notYet": 1200, "honoured": 4 }""");

        // The configuration is correct, merely early. Refusing it would make a developer come back
        // and finish the content model in a later phase.
        result.HasErrors.Should().BeFalse();
        result.Codes().Should().Equal(FieldConfigurationCodes.NotEnforced);
        result.Diagnostics[0].Message.Should().Contain(DeferredSettingFieldType.Phase);
    }

    /// <remarks>
    /// The media picker settings were the deferred ones until P5, and are now enforced on the
    /// publish path (task P5-19). Asserted here so that the day one of them is quietly moved back
    /// behind <c>notEnforcedUntil</c>, something says so.
    /// </remarks>
    [Fact]
    public void TheMediaPickerSettingsAreStoredWithNothingReportedAtAll()
    {
        var result = _validator.Validate(
            FieldTypeKeys.Media,
            """{ "allowedTypes": ["Image"], "minWidth": 1200, "aspectRatio": "16:9" }""");

        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EverythingWrongIsReportedAtOnce()
    {
        var result = _validator.Validate(
            FieldTypeKeys.PlainText,
            """{ "maxLength": 0, "pattern": "([a-z", "nonsense": 1 }""");

        // A developer fixing a zone one diagnostic per round trip is the reason to collect rather
        // than return on the first problem.
        result.Codes().Should().BeEquivalentTo([
            FieldConfigurationCodes.SettingRange,
            FieldConfigurationCodes.SettingFormat,
            FieldConfigurationCodes.UnknownSetting,
        ]);
    }

    /// <summary>The validator over the field types a deployment registers.</summary>
    private static IFieldConfigurationValidator Validator()
    {
        var provider = new ServiceCollection()
            .AddSingleton<IContentSanitizer, RecordingSanitizer>()
            .AddCmsFieldTypes()
            .AddCmsFieldType<DeferredSettingFieldType>()
            .BuildServiceProvider();

        return provider.GetRequiredService<IFieldConfigurationValidator>();
    }
}
