namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// Stable diagnostic codes raised when a zone or block-type property's <c>ConfigurationJson</c> is
/// checked against its field type's schema (spec section 7.2).
/// </summary>
/// <remarks>
/// Namespaced <c>config.*</c> to keep them apart from the <c>field.*</c> codes in
/// <see cref="FieldValidationCodes"/>. The two are raised by different people at different times: a
/// <c>field.*</c> code is a content editor's problem with a value, a <c>config.*</c> code is a
/// developer's problem with the structure, and the structure admin screens are the only place the
/// latter should ever surface.
/// </remarks>
public static class FieldConfigurationCodes
{
    /// <summary>The configuration names a field type that is not registered.</summary>
    public const string UnknownFieldType = "config.fieldType.unknown";

    /// <summary>The configuration is not well-formed JSON.</summary>
    public const string Malformed = "config.malformed";

    /// <summary>The configuration is well-formed JSON but not an object.</summary>
    public const string Shape = "config.shape";

    /// <summary>The configuration carries a setting the field type does not declare.</summary>
    public const string UnknownSetting = "config.setting.unknown";

    /// <summary>A setting holds a value of the wrong JSON type.</summary>
    public const string SettingType = "config.setting.type";

    /// <summary>A string setting does not have the syntax its field type has to parse.</summary>
    public const string SettingFormat = "config.setting.format";

    /// <summary>A numeric setting is outside the range its field type can honour.</summary>
    public const string SettingRange = "config.setting.range";

    /// <summary>A setting holds a value outside the closed set the field type accepts.</summary>
    public const string SettingValue = "config.setting.value";

    /// <summary>A lower bound is above the upper bound it is paired with.</summary>
    public const string RangeInverted = "config.range.inverted";

    /// <summary>
    /// The configuration carries <c>required</c>, which the <c>IsRequired</c> column owns.
    /// </summary>
    public const string RequiredReserved = "config.required.reserved";

    /// <summary>
    /// A setting is accepted and stored, but the phase that honours it has not shipped yet.
    /// </summary>
    /// <remarks>A warning, never an error: the configuration is correct, merely early.</remarks>
    public const string NotEnforced = "config.setting.notEnforced";
}
