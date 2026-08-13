using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Core.Tests.Structure;

/// <summary>
/// The shape a content-model key must have (task P1-21, spec section 8.5).
/// </summary>
/// <remarks>
/// These rules only ever run once per key, at creation, because a key can never be changed
/// afterwards. That makes the boundaries worth pinning: everything admitted here is admitted
/// forever.
/// </remarks>
public class ContentKeysTests
{
    [Theory]
    [InlineData("hero")]
    [InlineData("heroImage")]
    [InlineData("marketing-landing")]
    [InlineData("marketing_landing")]
    [InlineData("a1")]
    [InlineData("section-2-body")]
    public void AUsableKeyIsAccepted(string key) =>
        ContentKeys.Validate(key).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(null, StructureCodes.KeyRequired)]
    [InlineData("", StructureCodes.KeyRequired)]
    [InlineData("   ", StructureCodes.KeyRequired)]
    [InlineData("9lives", StructureCodes.KeyFormat)]
    [InlineData("-leading", StructureCodes.KeyFormat)]
    [InlineData("trailing-", StructureCodes.KeyFormat)]
    [InlineData("double--hyphen", StructureCodes.KeyFormat)]
    [InlineData("has space", StructureCodes.KeyFormat)]
    [InlineData("has.dot", StructureCodes.KeyFormat)]
    [InlineData("hero!", StructureCodes.KeyFormat)]
    public void AnUnusableKeyIsRefusedWithItsReason(string? key, string code)
    {
        var result = ContentKeys.Validate(key);

        result.HasErrors.Should().BeTrue();
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(code);
    }

    [Fact]
    public void AKeyLongerThanItsColumnIsRefused()
    {
        var result = ContentKeys.Validate("k" + new string('e', FieldLengths.ContentKey));

        // Reported as too long rather than left for the database, where the same mistake surfaces
        // as a truncation or an error naming a column instead of a field.
        result.Diagnostics.Should().ContainSingle().Which.Code.Should().Be(StructureCodes.TooLong);
    }

    [Fact]
    public void TheOffendingMemberIsNamed()
    {
        ContentKeys.Validate("9lives", "templateKey")
            .Diagnostics.Should().ContainSingle()
            .Which.RelativePath.Should().Be("templateKey");
    }
}
