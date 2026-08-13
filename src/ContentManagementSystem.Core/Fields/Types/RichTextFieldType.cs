using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// Formatted prose, authored as markdown or as HTML (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "richText", "format": "markdown"|"html", "value": "…" }</c>. The format is
/// carried in the payload rather than in configuration because it describes the value that was
/// written: a property switched from markdown to HTML must still be able to read what is already
/// stored.
/// <para>
/// Configuration keys: <c>required</c>, <c>maxLength</c>, <c>profile</c> (<c>basic</c> or
/// <c>extended</c>, spec section 20.2).
/// </para>
/// </remarks>
public sealed class RichTextFieldType : FieldTypeBase
{
    /// <summary>The member naming how the stored value is written.</summary>
    private const string FormatMember = "format";

    /// <summary>Markdown source, converted to HTML and sanitized on the way to the page.</summary>
    public const string MarkdownFormat = "markdown";

    /// <summary>HTML, sanitized directly.</summary>
    public const string HtmlFormat = "html";

    private readonly IContentSanitizer _sanitizer;

    /// <summary>Creates the field type.</summary>
    /// <param name="sanitizer">Applies the allowlist profile to authored markup.</param>
    public RichTextFieldType(IContentSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);

        _sanitizer = sanitizer;
    }

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.RichText;

    /// <inheritdoc />
    public override string DisplayName => "Rich text";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities =>
        FieldTypeCapabilities.Searchable | FieldTypeCapabilities.Sanitizable;

    /// <inheritdoc />
    protected override bool IsEmpty(JsonElement value) =>
        base.IsEmpty(value) ||
        (value.ValueKind is JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()));

    /// <inheritdoc />
    protected override ValidationResult ValidateValue(
        JsonElement property,
        JsonElement value,
        FieldConfiguration configuration,
        ValidationMode mode)
    {
        List<ValidationDiagnostic>? diagnostics = null;

        if (ReadFormat(property) is null)
        {
            // Without a format the stored text is uninterpretable: rendering markdown as HTML shows
            // the source, and rendering HTML as markdown escapes the markup. Neither degrades
            // gracefully, so this is an error rather than a defaulted guess.
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.RichTextFormat,
                $"Rich text must declare a format of '{MarkdownFormat}' or '{HtmlFormat}'.",
                ValidationSeverity.Error,
                FormatMember));
        }

        if (value.ValueKind is not JsonValueKind.String)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.Shape,
                "Expected a text value.",
                ValidationSeverity.Error,
                ValueMember));

            return Result(diagnostics);
        }

        var text = value.GetString()!;

        if (configuration.GetInt32("maxLength") is { } maxLength && text.Length > maxLength)
        {
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.MaxLength,
                $"Use at most {maxLength} characters; this is {text.Length}.",
                ValidationSeverity.Error,
                ValueMember));
        }

        if (configuration.GetString("profile") is { Length: > 0 } profile && !TryReadProfile(profile, out _))
        {
            // Reported, not enforced: the value is sanitized under the Basic profile regardless, so
            // a mistyped profile can only ever be more restrictive than intended, never less.
            Add(ref diagnostics, new ValidationDiagnostic(
                FieldValidationCodes.RichTextProfile,
                $"'{profile}' is not a rich text sanitization profile; 'basic' was applied instead.",
                ValidationSeverity.Warning));
        }

        return Result(diagnostics);
    }

    /// <inheritdoc />
    /// <remarks>
    /// HTML is sanitized here, on the way into the database. Markdown is stored exactly as authored
    /// and sanitized after conversion instead (P1-19): the raw HTML that markdown permits cannot be
    /// cleaned without first parsing the markdown around it, and rewriting an author's source to
    /// whatever a round trip through Markdig produces would make the editor lose their formatting on
    /// every save. Either way the markup reaching a browser has been through the allowlist, and the
    /// render-time pass (ADR 0008) covers both.
    /// </remarks>
    public override ValueTask<JsonElement> SanitizeAsync(
        JsonElement value,
        FieldConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();

        if (ReadFormat(value) != HtmlFormat || GetStringValue(value) is not { Length: > 0 } html)
        {
            return ValueTask.FromResult(value);
        }

        var profile = TryReadProfile(configuration.GetString("profile"), out var configured)
            ? configured
            : SanitizationProfile.Basic;

        var sanitized = _sanitizer.Sanitize(html, profile);

        return ValueTask.FromResult(
            string.Equals(sanitized, html, StringComparison.Ordinal)
                ? value
                : StoredProperty.WithStringMember(value, ValueMember, sanitized));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Markup is stripped from both formats. Markdown emphasis markers survive into the index, which
    /// costs a rare false match on a word neighbouring an asterisk and saves a Markdig conversion
    /// per property per publish.
    /// </remarks>
    public override string ExtractSearchText(JsonElement value) => SearchText.FromHtml(GetStringValue(value));

    private static string? ReadFormat(JsonElement property)
    {
        if (property.ValueKind is not JsonValueKind.Object ||
            !property.TryGetProperty(FormatMember, out var format) ||
            format.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var declared = format.GetString();

        return declared is MarkdownFormat or HtmlFormat ? declared : null;
    }

    private static bool TryReadProfile(string? configured, out SanitizationProfile profile)
    {
        // Developer is deliberately unreachable from here. It permits iframes and data attributes,
        // and the role gate that justifies them lives on the html field type, not on this one.
        switch (configured)
        {
            case "basic":
                profile = SanitizationProfile.Basic;
                return true;

            case "extended":
                profile = SanitizationProfile.Extended;
                return true;

            default:
                profile = SanitizationProfile.Basic;
                return configured is null or "";
        }
    }
}
