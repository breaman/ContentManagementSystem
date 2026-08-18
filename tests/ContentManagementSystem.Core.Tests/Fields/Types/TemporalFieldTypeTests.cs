using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// <c>date</c> and <c>dateTime</c> (task P1-10, spec section 7.1).
/// </summary>
public class TemporalFieldTypeTests
{
    private readonly DateFieldType _date = new();
    private readonly DateTimeFieldType _dateTime = new();

    [Test]
    public async Task AnIsoDateIsAccepted()
    {
        var result = await _date.ValidateAsync("""{ "type": "date", "value": "2026-08-12" }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    [Arguments("08/12/2026")]
    [Arguments("12 August 2026")]
    [Arguments("2026-8-12")]
    [Arguments("2026-02-30")]
    [Arguments("2026-08-12T00:00:00Z")]
    public async Task OtherDateNotationsAreRefused(string value)
    {
        var result = await _date.ValidateAsync($$"""{ "type": "date", "value": "{{value}}" }""");

        // 08/12/2026 is two different days depending on who reads it. Parsing exactly is what stops
        // the system picking one silently.
        result.Codes().Should().Equal(FieldValidationCodes.DateFormat);
    }

    [Test]
    public async Task ADateBeforeMinIsRejected()
    {
        var result = await _date.ValidateAsync(
            """{ "type": "date", "value": "2026-08-12" }""",
            """{ "min": "2026-09-01" }""");

        result.Codes().Should().Equal(FieldValidationCodes.Min);
    }

    [Test]
    public async Task ADateAfterMaxIsRejected()
    {
        var result = await _date.ValidateAsync(
            """{ "type": "date", "value": "2026-12-25" }""",
            """{ "max": "2026-09-01" }""");

        result.Codes().Should().Equal(FieldValidationCodes.Max);
    }

    [Test]
    [Arguments("2026-08-12T09:30:00Z")]
    [Arguments("2026-08-12T09:30:00+02:00")]
    [Arguments("2026-08-12T09:30:00.125Z")]
    public async Task AnInstantWithAnOffsetIsAccepted(string value)
    {
        var result = await _dateTime.ValidateAsync($$"""{ "type": "dateTime", "value": "{{value}}" }""");

        result.IsValid.Should().BeTrue();
    }

    [Test]
    public async Task AnInstantWithNoOffsetIsRejected()
    {
        var result = await _dateTime.ValidateAsync(
            """{ "type": "dateTime", "value": "2026-08-12T09:30:00" }""");

        // It means one thing to the browser that submitted it, another to the server that stores
        // it, and a third to the scheduler that acts on it.
        result.Codes().Should().Equal(FieldValidationCodes.DateTimeOffset);
    }

    [Test]
    public async Task ADateOnlyValueIsNotAnInstant()
    {
        var result = await _dateTime.ValidateAsync("""{ "type": "dateTime", "value": "2026-08-12" }""");

        result.Codes().Should().Equal(FieldValidationCodes.DateTimeOffset);
    }

    [Test]
    public async Task NonsenseIsReportedAsAFormatProblem()
    {
        var result = await _dateTime.ValidateAsync("""{ "type": "dateTime", "value": "tomorrow" }""");

        result.Codes().Should().Equal(FieldValidationCodes.DateTimeFormat);
    }

    [Test]
    public async Task BoundsAreComparedAsInstantsRatherThanAsText()
    {
        var result = await _dateTime.ValidateAsync(
            """{ "type": "dateTime", "value": "2026-08-12T09:30:00+02:00" }""",
            """{ "min": "2026-08-12T08:00:00Z" }""");

        // 09:30+02:00 is 07:30 UTC, which is before the bound even though the text reads later.
        result.Codes().Should().Equal(FieldValidationCodes.Min);
    }

    [Test]
    public async Task AnInstantAfterMaxIsRejected()
    {
        var result = await _dateTime.ValidateAsync(
            """{ "type": "dateTime", "value": "2026-08-12T09:30:00Z" }""",
            """{ "max": "2026-08-12T08:00:00Z" }""");

        result.Codes().Should().Equal(FieldValidationCodes.Max);
    }
}
