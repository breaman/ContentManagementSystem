namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// Names of the field configuration settings both the server and the backoffice read
/// (spec section 7.2).
/// </summary>
/// <remarks>
/// A setting's name is a contract between three places: the field type that declares it, the
/// validator that enforces it, and the editor component that honours it. The first two are in
/// <c>Core</c> and the third is in <c>Client</c>, which cannot see <c>Core</c> — so a name written
/// as a literal in both would be one rename away from an editor that quietly stops reading a
/// setting the structure screen still lets a developer set.
/// <para>
/// Only the settings that cross that boundary are here. A setting no editor reads stays a literal
/// beside the field type that declares it, where it is easier to see next to its description.
/// </para>
/// <para>
/// These strings are stored in <c>ConfigurationJson</c> on zones and block type properties, and are
/// captured verbatim into revision snapshots, so they are as immutable as a field type key:
/// configuration is closed (ADR-0015) and a renamed setting is refused on the next structure save.
/// </para>
/// </remarks>
public static class FieldSettingNames
{
    /// <summary>Fewest characters a text value may contain.</summary>
    public const string MinLength = "minLength";

    /// <summary>Most characters a text value may contain. Enforced; publishing fails past it.</summary>
    public const string MaxLength = "maxLength";

    /// <summary>
    /// Length the editor's counter starts warning at (task P6-12).
    /// </summary>
    /// <remarks>
    /// <strong>Advisory only.</strong> Nothing on the server reads it and no value is refused for
    /// passing it — that is the whole distinction from <see cref="MaxLength"/>. It exists because
    /// "a meta description over 160 characters gets truncated in results" is guidance an author
    /// wants while typing, not a rule that should stop them publishing.
    /// </remarks>
    public const string SoftLimit = "softLimit";

    /// <summary>Regular expression every value must match.</summary>
    public const string Pattern = "pattern";

    /// <summary>What to tell an editor when a value does not match <see cref="Pattern"/>.</summary>
    public const string PatternMessage = "patternMessage";

    /// <summary>How permissive the HTML allowlist is for a rich text property.</summary>
    public const string Profile = "profile";

    /// <summary>The values a choice property may hold.</summary>
    public const string Options = "options";

    /// <summary>Whether several values may be chosen, storing an array rather than one value.</summary>
    public const string Multiple = "multiple";

    /// <summary>Fewest items a list must contain, or fewest options that must be chosen.</summary>
    public const string Min = "min";

    /// <summary>Most items a list may contain, or most options that may be chosen.</summary>
    public const string Max = "max";

    /// <summary>Increment a number value must be a multiple of.</summary>
    public const string Step = "step";

    /// <summary>The colours a property is allowed to hold, each written as <c>#RRGGBB</c>.</summary>
    public const string Palette = "palette";

    /// <summary>Keys of the block types an editor may add to a blocks property.</summary>
    public const string AllowedBlockTypes = "allowedBlockTypes";

    /// <summary>Whether a block may contain further blocks.</summary>
    public const string AllowNesting = "allowNesting";

    /// <summary>Kinds of destination a link property accepts.</summary>
    public const string AllowedKinds = "allowedKinds";

    /// <summary>Template keys a page reference may point at.</summary>
    public const string AllowedTemplates = "allowedTemplates";

    /// <summary>Block type keys a reusable placement may hold, or media types a picker accepts.</summary>
    public const string AllowedTypes = "allowedTypes";
}
