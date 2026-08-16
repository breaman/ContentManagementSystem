using System.Text.Json;

using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Fields.Configuration;

/// <summary>
/// A field type declaring a setting it does not honour yet, so the deferred-setting machinery keeps
/// its tests once every shipping field type has caught up.
/// </summary>
/// <remarks>
/// <c>media</c>'s three picker settings used to serve this purpose and stopped in P5, when the
/// publish check began enforcing them. Re-pointing the tests at whichever field type happens to be
/// behind would put them back in the same position a phase later; a stub owned by the test project
/// keeps <see cref="FieldConfigurationSetting.NotEnforcedUntil"/> covered permanently, which matters
/// because the alternative to that mechanism — refusing configuration outright, or accepting it
/// silently — is what it was introduced to avoid.
/// </remarks>
internal sealed class DeferredSettingFieldType : FieldTypeBase
{
    /// <summary>The key this stub registers under.</summary>
    public const string TypeKey = "testDeferred";

    /// <summary>The setting that is declared but not yet honoured.</summary>
    public const string DeferredSetting = "notYet";

    /// <summary>The phase it says it will be honoured in.</summary>
    public const string Phase = "P99";

    /// <inheritdoc />
    public override string Key => TypeKey;

    /// <inheritdoc />
    public override string DisplayName => "Deferred setting";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.None;

    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = new(
        [
            FieldConfigurationSetting.Integer(
                DeferredSetting,
                "A setting whose enforcing phase has not shipped.",
                minimum: 1,
                notEnforcedUntil: Phase),
            FieldConfigurationSetting.Integer("honoured", "A setting that is honoured today.", minimum: 1),
        ]);

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode) => ValidationResult.Success;
}
