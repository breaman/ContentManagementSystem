using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Configuration;

/// <summary>
/// Checks a zone or block-type property's <c>ConfigurationJson</c> against its field type's schema
/// (spec section 7.2).
/// </summary>
/// <remarks>
/// Called on every structure write — zone create and update, block-type property create and update,
/// and the additive schema sync in <c>P1-26</c> — so that a configuration the field type cannot
/// honour is refused at the point it would be stored, rather than discovered by an editor whose
/// value will not publish.
/// </remarks>
public interface IFieldConfigurationValidator
{
    /// <summary>
    /// Validates one configuration blob.
    /// </summary>
    /// <param name="fieldTypeKey">Key of the field type the zone or property is bound to.</param>
    /// <param name="configurationJson">
    /// The <c>ConfigurationJson</c> to store. Null, empty, or whitespace is always valid — a zone
    /// need not configure anything.
    /// </param>
    /// <returns>
    /// Diagnostics found, each carrying a stable <see cref="FieldConfigurationCodes"/> code and, for
    /// anything concerning one setting, that setting's name as its relative path.
    /// </returns>
    ValidationResult Validate(string fieldTypeKey, string? configurationJson);
}
