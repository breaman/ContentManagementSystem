namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// The JSON type one configuration setting may hold.
/// </summary>
/// <remarks>
/// Deliberately the five shapes <see cref="FieldConfiguration"/> can read. A setting of a kind the
/// accessors cannot return would be a setting the field type could declare and never use.
/// </remarks>
public enum FieldSettingKind
{
    /// <summary>A JSON boolean, read with <see cref="FieldConfiguration.GetBoolean"/>.</summary>
    Boolean = 0,

    /// <summary>A whole number, read with <see cref="FieldConfiguration.GetInt32"/>.</summary>
    Integer = 1,

    /// <summary>A number with a fractional part, read with <see cref="FieldConfiguration.GetDecimal"/>.</summary>
    Number = 2,

    /// <summary>A JSON string, read with <see cref="FieldConfiguration.GetString"/>.</summary>
    Text = 3,

    /// <summary>An array of strings, read with <see cref="FieldConfiguration.GetStringArray"/>.</summary>
    TextList = 4,
}

/// <summary>
/// An extra syntactic constraint on a <see cref="FieldSettingKind.Text"/> setting, or on each item of
/// a <see cref="FieldSettingKind.TextList"/>.
/// </summary>
/// <remarks>
/// Every member here is a shape the platform must be able to parse to honour the setting at all — a
/// <c>pattern</c> that does not compile and a <c>min</c> date that is not a date are both
/// configuration the field type will ignore at content-validation time, which is exactly what this
/// task exists to catch before it is stored (spec section 7.2).
/// </remarks>
public enum FieldSettingFormat
{
    /// <summary>No constraint beyond being a string.</summary>
    None = 0,

    /// <summary>A .NET regular expression that compiles.</summary>
    RegularExpression = 1,

    /// <summary>An ISO-8601 calendar date, <c>2026-08-13</c>.</summary>
    Date = 2,

    /// <summary>An ISO-8601 instant carrying a UTC offset.</summary>
    DateTime = 3,

    /// <summary>An <c>#RRGGBB</c> colour.</summary>
    HexColor = 4,
}

/// <summary>
/// One setting a field type accepts in its <c>ConfigurationJson</c> (spec section 7.2).
/// </summary>
/// <remarks>
/// Declared rather than inferred: a field type that reads a setting it never declares gets that
/// setting rejected on zone save, so the two halves are checked against each other by the
/// configuration contract test rather than kept in step by hand.
/// <para>
/// Use the static factories below rather than the initialiser — they name the kind, which is the
/// part that decides how the value is read.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// FieldConfigurationSetting.Integer("maxLength", "Longest value an editor may enter.", minimum: 1)
/// </code>
/// </example>
public sealed record FieldConfigurationSetting
{
    /// <summary>The member name as it appears in <c>ConfigurationJson</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The JSON type the value must have.</summary>
    public required FieldSettingKind Kind { get; init; }

    /// <summary>What the setting does, shown to a developer configuring a zone.</summary>
    public required string Description { get; init; }

    /// <summary>Extra syntactic constraint on a string value, or on each item of a list.</summary>
    public FieldSettingFormat Format { get; init; }

    /// <summary>Smallest value a numeric setting may hold, if any.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>Whether <see cref="Minimum"/> is a bound the value must exceed rather than reach.</summary>
    public bool MinimumExclusive { get; init; }

    /// <summary>Largest value a numeric setting may hold, if any.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>
    /// The complete set of values a string setting, or each item of a list setting, may hold.
    /// Empty when the value is unconstrained.
    /// </summary>
    public IReadOnlyList<string> AllowedValues { get; init; } = [];

    /// <summary>
    /// The phase that starts honouring this setting, or null when it is honoured today.
    /// </summary>
    /// <remarks>
    /// Several field types were stubbed to their contract in <c>P1-11</c> and finish in the phase
    /// that owns their tables — <c>media</c>'s <c>allowedTypes</c> in P5, <c>pageReference</c>'s
    /// <c>allowedTemplates</c> in P3. Declaring those now and warning is better than either
    /// alternative: rejecting them outright makes a developer wait to write configuration that is
    /// correct, and accepting them silently promises enforcement that does not happen yet.
    /// </remarks>
    public string? NotEnforcedUntil { get; init; }

    /// <summary>Declares a boolean setting.</summary>
    /// <param name="name">Member name in <c>ConfigurationJson</c>.</param>
    /// <param name="description">What the setting does.</param>
    /// <param name="notEnforcedUntil">Phase that starts honouring it, if not this one.</param>
    public static FieldConfigurationSetting Boolean(
        string name,
        string description,
        string? notEnforcedUntil = null) =>
        new()
        {
            Name = name,
            Kind = FieldSettingKind.Boolean,
            Description = description,
            NotEnforcedUntil = notEnforcedUntil,
        };

    /// <summary>Declares a whole-number setting.</summary>
    /// <param name="name">Member name in <c>ConfigurationJson</c>.</param>
    /// <param name="description">What the setting does.</param>
    /// <param name="minimum">Smallest permitted value, if any.</param>
    /// <param name="maximum">Largest permitted value, if any.</param>
    /// <param name="notEnforcedUntil">Phase that starts honouring it, if not this one.</param>
    public static FieldConfigurationSetting Integer(
        string name,
        string description,
        int? minimum = null,
        int? maximum = null,
        string? notEnforcedUntil = null) =>
        new()
        {
            Name = name,
            Kind = FieldSettingKind.Integer,
            Description = description,
            Minimum = minimum,
            Maximum = maximum,
            NotEnforcedUntil = notEnforcedUntil,
        };

    /// <summary>Declares a numeric setting that may have a fractional part.</summary>
    /// <param name="name">Member name in <c>ConfigurationJson</c>.</param>
    /// <param name="description">What the setting does.</param>
    /// <param name="minimum">Smallest permitted value, if any.</param>
    /// <param name="minimumExclusive">Whether the value must exceed <paramref name="minimum"/> rather than reach it.</param>
    /// <param name="maximum">Largest permitted value, if any.</param>
    /// <param name="notEnforcedUntil">Phase that starts honouring it, if not this one.</param>
    public static FieldConfigurationSetting Number(
        string name,
        string description,
        decimal? minimum = null,
        bool minimumExclusive = false,
        decimal? maximum = null,
        string? notEnforcedUntil = null) =>
        new()
        {
            Name = name,
            Kind = FieldSettingKind.Number,
            Description = description,
            Minimum = minimum,
            MinimumExclusive = minimumExclusive,
            Maximum = maximum,
            NotEnforcedUntil = notEnforcedUntil,
        };

    /// <summary>Declares a string setting.</summary>
    /// <param name="name">Member name in <c>ConfigurationJson</c>.</param>
    /// <param name="description">What the setting does.</param>
    /// <param name="format">Extra syntactic constraint on the value.</param>
    /// <param name="allowedValues">The complete set of permitted values, if it is closed.</param>
    /// <param name="notEnforcedUntil">Phase that starts honouring it, if not this one.</param>
    public static FieldConfigurationSetting Text(
        string name,
        string description,
        FieldSettingFormat format = FieldSettingFormat.None,
        IReadOnlyList<string>? allowedValues = null,
        string? notEnforcedUntil = null) =>
        new()
        {
            Name = name,
            Kind = FieldSettingKind.Text,
            Description = description,
            Format = format,
            AllowedValues = allowedValues ?? [],
            NotEnforcedUntil = notEnforcedUntil,
        };

    /// <summary>Declares a setting holding a list of strings.</summary>
    /// <param name="name">Member name in <c>ConfigurationJson</c>.</param>
    /// <param name="description">What the setting does.</param>
    /// <param name="format">Extra syntactic constraint applied to every item.</param>
    /// <param name="allowedValues">The complete set of permitted items, if it is closed.</param>
    /// <param name="notEnforcedUntil">Phase that starts honouring it, if not this one.</param>
    public static FieldConfigurationSetting TextList(
        string name,
        string description,
        FieldSettingFormat format = FieldSettingFormat.None,
        IReadOnlyList<string>? allowedValues = null,
        string? notEnforcedUntil = null) =>
        new()
        {
            Name = name,
            Kind = FieldSettingKind.TextList,
            Description = description,
            Format = format,
            AllowedValues = allowedValues ?? [],
            NotEnforcedUntil = notEnforcedUntil,
        };
}

/// <summary>
/// Two numeric or date settings that bound each other, such as <c>min</c> and <c>max</c>.
/// </summary>
/// <param name="LowerName">Setting holding the lower bound.</param>
/// <param name="UpperName">Setting holding the upper bound.</param>
/// <remarks>
/// The one rule in this model that JSON Schema cannot express, and the one most worth having: a zone
/// configured <c>{ "min": 5, "max": 2 }</c> is not merely odd, it is a zone no value can ever
/// satisfy, and the editor discovers that only when someone fails to publish.
/// </remarks>
public readonly record struct FieldSettingRange(string LowerName, string UpperName);

/// <summary>
/// Everything one field type accepts in its <c>ConfigurationJson</c> (spec section 7.2).
/// </summary>
/// <remarks>
/// Configuration is closed: a member the schema does not declare is refused on zone save, because
/// the alternative is a mistyped <c>maxlength</c> that persists happily and does nothing. That
/// closedness is the reason the schema is declared in C# beside the field type rather than authored
/// as a JSON Schema document and interpreted here — see ADR 0015. The JSON Schema the spec calls for
/// is still published, generated from this and served to the editor by the read-only
/// <c>/field-types</c> endpoint.
/// <para>
/// <c>required</c> is not a setting and cannot be declared as one. It is carried by the
/// <c>IsRequired</c> column on <c>Zone</c> and <c>BlockTypeProperty</c> and reaches a field type
/// through <see cref="FieldConfiguration.IsRequired"/>; a second copy inside the configuration blob
/// would be free to disagree with the column, and nothing would say which one wins.
/// </para>
/// </remarks>
public sealed class FieldConfigurationSchema
{
    /// <summary>The name reserved for the platform's own required flag.</summary>
    public const string RequiredSettingName = "required";

    /// <summary>A field type that accepts no configuration at all.</summary>
    public static FieldConfigurationSchema Empty { get; } = new([], []);

    private readonly Dictionary<string, FieldConfigurationSetting> _byName;

    /// <summary>
    /// Declares a schema.
    /// </summary>
    /// <param name="settings">The settings accepted.</param>
    /// <param name="ranges">Pairs of settings that bound each other.</param>
    /// <exception cref="ArgumentException">
    /// A setting is named <see cref="RequiredSettingName"/>, two settings share a name, or a range
    /// names a setting that was not declared.
    /// </exception>
    public FieldConfigurationSchema(
        IEnumerable<FieldConfigurationSetting> settings,
        IEnumerable<FieldSettingRange>? ranges = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _byName = new Dictionary<string, FieldConfigurationSetting>(StringComparer.Ordinal);

        // Declaration order is kept explicitly rather than read back off the dictionary: it is the
        // order the generated JSON Schema and the zone configuration form present settings in.
        var ordered = new List<FieldConfigurationSetting>();

        foreach (var setting in settings)
        {
            if (string.Equals(setting.Name, RequiredSettingName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"'{RequiredSettingName}' cannot be declared as a configuration setting. It is " +
                    "carried by the IsRequired column and reaches a field type through " +
                    $"{nameof(FieldConfiguration)}.{nameof(FieldConfiguration.IsRequired)}.",
                    nameof(settings));
            }

            if (!_byName.TryAdd(setting.Name, setting))
            {
                throw new ArgumentException(
                    $"Setting '{setting.Name}' is declared twice.",
                    nameof(settings));
            }

            ordered.Add(setting);
        }

        Settings = ordered.AsReadOnly();
        Ranges = [.. ranges ?? []];

        foreach (var range in Ranges)
        {
            // A range naming a setting that does not exist would silently never fire, which is the
            // failure mode this whole type is built to make impossible one level up.
            if (!_byName.ContainsKey(range.LowerName) || !_byName.ContainsKey(range.UpperName))
            {
                throw new ArgumentException(
                    $"Range '{range.LowerName}'–'{range.UpperName}' names a setting that is not declared.",
                    nameof(ranges));
            }
        }
    }

    /// <summary>The settings accepted, in declaration order.</summary>
    public IReadOnlyList<FieldConfigurationSetting> Settings { get; }

    /// <summary>Pairs of settings that bound each other.</summary>
    public IReadOnlyList<FieldSettingRange> Ranges { get; }

    /// <summary>Looks up a setting by its member name.</summary>
    /// <param name="name">Member name, case-sensitive as JSON is.</param>
    /// <returns>The setting, or null when this schema does not declare it.</returns>
    public FieldConfigurationSetting? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Returns this schema plus more settings, for a field type that adds to what its base accepts.
    /// </summary>
    /// <param name="settings">Settings to add. A name already declared replaces the inherited one.</param>
    /// <param name="ranges">Ranges to add to those already declared.</param>
    /// <returns>A new schema; this one is unchanged.</returns>
    /// <example>
    /// <code>
    /// public override FieldConfigurationSchema ConfigurationSchema { get; } =
    ///     ListConfigurationSchema.Extend([FieldConfigurationSetting.Boolean("allowNesting", "…")]);
    /// </code>
    /// </example>
    public FieldConfigurationSchema Extend(
        IEnumerable<FieldConfigurationSetting> settings,
        IEnumerable<FieldSettingRange>? ranges = null)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var merged = new List<FieldConfigurationSetting>(Settings);

        foreach (var setting in settings)
        {
            // A redeclared name replaces the inherited setting in place, so a subclass tightening
            // one of its base's settings does not also move it to the end of the editor's form.
            var existing = merged.FindIndex(declared =>
                string.Equals(declared.Name, setting.Name, StringComparison.Ordinal));

            if (existing >= 0)
            {
                merged[existing] = setting;
            }
            else
            {
                merged.Add(setting);
            }
        }

        return new FieldConfigurationSchema(merged, [.. Ranges, .. ranges ?? []]);
    }
}
