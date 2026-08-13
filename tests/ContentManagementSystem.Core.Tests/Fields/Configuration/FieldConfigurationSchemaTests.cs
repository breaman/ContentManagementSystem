using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Configuration;

/// <summary>
/// The rules a field type's declared configuration schema has to satisfy (task P1-12, spec
/// section 7.2).
/// </summary>
public class FieldConfigurationSchemaTests
{
    [Fact]
    public void RequiredCannotBeDeclaredAsASetting()
    {
        var declare = () => new FieldConfigurationSchema(
            [FieldConfigurationSetting.Boolean("required", "Whether a value must be filled in.")]);

        // Two copies of the flag — the column and the blob — would be free to disagree, and nothing
        // in the model would say which one won.
        declare.Should().Throw<ArgumentException>().WithMessage("*IsRequired*");
    }

    [Fact]
    public void ASettingCannotBeDeclaredTwice()
    {
        var declare = () => new FieldConfigurationSchema(
            [
                FieldConfigurationSetting.Integer("max", "Largest value."),
                FieldConfigurationSetting.Text("max", "Latest date."),
            ]);

        declare.Should().Throw<ArgumentException>().WithMessage("*declared twice*");
    }

    [Fact]
    public void ARangeCannotNameASettingThatIsNotDeclared()
    {
        var declare = () => new FieldConfigurationSchema(
            [FieldConfigurationSetting.Integer("min", "Fewest items.")],
            [new FieldSettingRange("min", "max")]);

        // A range over a setting that does not exist would never fire, which is the silent failure
        // this whole model exists to make impossible one level up.
        declare.Should().Throw<ArgumentException>().WithMessage("*not declared*");
    }

    [Fact]
    public void ExtendingAddsToWhatTheBaseAccepts()
    {
        var extended = Counts.Extend([FieldConfigurationSetting.Boolean("allowNesting", "Nesting.")]);

        extended.Settings.Select(setting => setting.Name).Should().Equal("min", "max", "allowNesting");
        extended.Ranges.Should().Equal(new FieldSettingRange("min", "max"));
        Counts.Find("allowNesting").Should().BeNull("Extend returns a new schema rather than mutating");
    }

    [Fact]
    public void RedeclaringASettingReplacesItWhereItAlreadySat()
    {
        var extended = Counts.Extend(
            [FieldConfigurationSetting.Integer("min", "Fewest blocks an editor must add.", minimum: 1)]);

        // Order is what the zone configuration form presents settings in, so a tightened setting
        // should not also jump to the end of it.
        extended.Settings.Select(setting => setting.Name).Should().Equal("min", "max");
        extended.Find("min")!.Minimum.Should().Be(1);
    }

    private static FieldConfigurationSchema Counts { get; } = new(
        [
            FieldConfigurationSetting.Integer("min", "Fewest items.", minimum: 0),
            FieldConfigurationSetting.Integer("max", "Most items.", minimum: 0),
        ],
        [new FieldSettingRange("min", "max")]);
}
