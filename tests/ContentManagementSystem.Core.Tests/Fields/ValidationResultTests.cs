using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields;

/// <summary>
/// Covers the severity distinction the template-evolution rules depend on (task P1-08).
/// </summary>
public class ValidationResultTests
{
    [Fact]
    public void SuccessReportsNothing()
    {
        ValidationResult.Success.IsValid.Should().BeTrue();
        ValidationResult.Success.HasErrors.Should().BeFalse();
        ValidationResult.Success.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void AnErrorBlocksTheOperation()
    {
        var result = ValidationResult.Error("field.maxLength", "Value is 158 characters; the maximum is 120.");

        result.IsValid.Should().BeFalse();
        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle()
            .Which.Code.Should().Be("field.maxLength");
    }

    [Fact]
    public void AWarningIsReportedWithoutBlocking()
    {
        // This distinction is what makes "removing a zone keeps its data" implementable rather than
        // aspirational: orphaned content warns, it does not stop the save.
        var result = ValidationResult.Warning("property.orphaned", "No property with this key exists.");

        result.IsValid.Should().BeFalse("the diagnostic is still reported");
        result.HasErrors.Should().BeFalse("a warning does not block the save");
    }

    [Fact]
    public void AMixedResultBlocksOnItsError()
    {
        var result = ValidationResult.From(
        [
            new ValidationDiagnostic("zone.orphaned", "Retained as orphaned content.", ValidationSeverity.Warning),
            new ValidationDiagnostic("zone.required", "This zone must be filled in before publishing."),
        ]);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().HaveCount(2);
    }

    [Fact]
    public void AnEmptyDiagnosticSequenceCollapsesToSuccess()
    {
        ValidationResult.From([]).Should().BeSameAs(ValidationResult.Success);
    }

    [Fact]
    public void ADiagnosticDefaultsToBlocking()
    {
        // The safe default: a field type that forgets to state severity stops the publish rather
        // than letting bad content through silently.
        new ValidationDiagnostic("field.type", "Expected a text value.")
            .Severity.Should().Be(ValidationSeverity.Error);
    }

    [Fact]
    public void ADiagnosticCanPointInsideTheValueItCameFrom()
    {
        var result = ValidationResult.Error("field.link.pageId", "An internal link must carry 'pageId'.", "cta");

        result.Diagnostics[0].RelativePath.Should().Be("cta");
    }
}
