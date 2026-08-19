using System.Text.Json;

using AngleSharp.Dom;
using AngleSharp.Html.Parser;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// The authored-output accessibility rules of spec section 28 (task P9-10).
/// </summary>
/// <remarks>
/// Three of the section's rules are checks on markup an editor wrote, and they belong together
/// because they are all answered by parsing the same document: skipped heading levels, link text that
/// says nothing, and tables with no usable headers.
/// <para>
/// The other rules in section 28 are structural and are enforced elsewhere, which is why they are not
/// here. <c>h1</c> is absent from every sanitization profile, so the rich-text editor cannot offer
/// one. The <c>color</c> field type takes a <c>palette</c> of design-system tokens and refuses
/// anything outside it. <c>lang</c> comes from <c>SiteSettings.Culture</c> on the delivery document.
/// Alt text blocks a publish rather than warning, in <c>MediaContentValidator</c>.
/// </para>
/// <para>
/// <strong>Every diagnostic here is a warning.</strong> A publish an editor cannot complete because a
/// link says "read more" is a publish that happens through whatever route skips the check — and none
/// of these describes content that is broken, only content that is worse than it needs to be.
/// </para>
/// </remarks>
public interface IAuthoredAccessibilityValidator
{
    /// <summary>
    /// Checks every piece of authored markup in a payload.
    /// </summary>
    /// <param name="payload">The content being published.</param>
    /// <returns>What a publish should warn about, in the order the markup was found.</returns>
    IReadOnlyList<ValidationDiagnostic> Validate(ContentPayload payload);
}

/// <inheritdoc cref="IAuthoredAccessibilityValidator" />
/// <param name="fieldTypes">Which stored values carry markup.</param>
/// <remarks>
/// <strong>Which values to look at is asked of the field type registry, not of a list of keys.</strong>
/// <see cref="FieldTypeCapabilities.Sanitizable"/> already means "this value is markup an author
/// wrote", so a field type added later is checked by declaring what it is rather than by being added
/// here. The value's own shape is the one thing assumed: markup lives under <c>value</c> as a string,
/// which is true of both field types that carry the flag today.
/// <para>
/// The payload is walked recursively rather than zone by zone, because a rich-text property inside a
/// block is exactly as visible on the page as one in a zone, and a check that only reached the top
/// level would report a clean bill for a page built entirely out of blocks.
/// </para>
/// </remarks>
public sealed class AuthoredAccessibilityValidator(IFieldTypeRegistry fieldTypes)
    : IAuthoredAccessibilityValidator
{
    /// <summary>Link text that describes the act of clicking rather than the destination.</summary>
    /// <remarks>
    /// Compared after case folding, trimming, and stripping trailing punctuation, so "Click here!"
    /// and "click here" are one entry. Kept short deliberately: a longer list starts catching link
    /// text that is terse and perfectly clear, and a warning an editor learns to dismiss is worse
    /// than no warning.
    /// </remarks>
    private static readonly string[] Uninformative =
    [
        "click here", "here", "read more", "more", "learn more", "this", "this link",
        "link", "go", "continue", "details", "download", "click", "see more", "find out more",
    ];

    private static readonly HtmlParser Parser = new();

    /// <inheritdoc />
    public IReadOnlyList<ValidationDiagnostic> Validate(ContentPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (!payload.HasZones) return [];

        var diagnostics = new List<ValidationDiagnostic>();

        // One heading sequence for the whole page rather than one per zone. A reader moving by
        // heading does not know where a zone ends, so an h2 in one zone followed by an h4 in the next
        // is the same skip it would be inside one.
        var previousLevel = 1;

        foreach (var (path, markup) in Markup(payload.Zones, ContentPayloadMembers.Zones))
        {
            // Parsed as a document rather than as a fragment, so that the parser's own rules about
            // where an element may appear are applied — the same rules the browser will apply when it
            // renders this. Authored markup always lands in the body.
            var body = Parser.ParseDocument(markup).Body;

            if (body is null) continue;

            foreach (var element in body.QuerySelectorAll("*"))
            {
                switch (element.LocalName)
                {
                    case "h2" or "h3" or "h4" or "h5" or "h6":
                        var level = element.LocalName[1] - '0';

                        if (level > previousLevel + 1)
                        {
                            diagnostics.Add(new ValidationDiagnostic(
                                AccessibilityCodes.HeadingSkipped,
                                $"'{Text(element)}' is an h{level} directly under an h{previousLevel}. " +
                                "Screen readers navigate by heading level, so a skipped level reads as " +
                                $"a missing section. Use an h{previousLevel + 1}.",
                                ValidationSeverity.Warning,
                                path));
                        }

                        previousLevel = level;
                        break;

                    case "a" when Uninformative.Contains(Normalize(Text(element)), StringComparer.Ordinal):
                        diagnostics.Add(new ValidationDiagnostic(
                            AccessibilityCodes.LinkTextUninformative,
                            $"The link reading '{Text(element)}' does not say where it goes. A screen " +
                            "reader can list every link on a page, and a list of these is a list of " +
                            "nothing — say what is at the other end instead.",
                            ValidationSeverity.Warning,
                            path));
                        break;

                    case "a" when IsBareUrl(Text(element)):
                        diagnostics.Add(new ValidationDiagnostic(
                            AccessibilityCodes.LinkTextUninformative,
                            "A link's text is a bare URL, which a screen reader reads out character by " +
                            "character. Use words that describe the destination.",
                            ValidationSeverity.Warning,
                            path));
                        break;

                    case "table" when element.QuerySelector("th") is null:
                        diagnostics.Add(new ValidationDiagnostic(
                            AccessibilityCodes.TableWithoutHeaders,
                            "This table has no header cells, so every cell in it is read without the " +
                            "column or row it belongs to.",
                            ValidationSeverity.Warning,
                            path));
                        break;

                    case "th" when string.IsNullOrEmpty(element.GetAttribute("scope")):
                        diagnostics.Add(new ValidationDiagnostic(
                            AccessibilityCodes.TableHeaderWithoutScope,
                            $"The header cell '{Text(element)}' has no scope, so nothing says whether it " +
                            "heads a row or a column. Add scope=\"col\" or scope=\"row\".",
                            ValidationSeverity.Warning,
                            path));
                        break;
                }
            }
        }

        return diagnostics;
    }

    /// <summary>
    /// Every markup-bearing value in a payload, with the path it was found at.
    /// </summary>
    /// <param name="element">The node being walked.</param>
    /// <param name="path">Where in the document it sits.</param>
    /// <returns>The markup, in document order.</returns>
    /// <remarks>
    /// Depth-first and in document order, which is what makes the heading sequence mean anything: the
    /// warnings describe the page as a reader meets it rather than as the serializer happened to
    /// write it.
    /// </remarks>
    private IEnumerable<(string Path, string Markup)> Markup(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (IsMarkupValue(element, out var markup))
                {
                    if (!string.IsNullOrWhiteSpace(markup)) yield return (path, markup);

                    // A markup value has no children worth walking: its `value` is a string, and
                    // descending into the rest of the envelope would find configuration, not content.
                    yield break;
                }

                foreach (var member in element.EnumerateObject())
                {
                    foreach (var found in Markup(member.Value, $"{path}.{member.Name}"))
                    {
                        yield return found;
                    }
                }

                break;

            case JsonValueKind.Array:
                var index = 0;

                foreach (var item in element.EnumerateArray())
                {
                    foreach (var found in Markup(item, $"{path}[{index}]"))
                    {
                        yield return found;
                    }

                    index++;
                }

                break;
        }
    }

    /// <summary>Whether this envelope was written by a field type that stores markup.</summary>
    /// <param name="element">The candidate object.</param>
    /// <param name="markup">The markup it holds, when it holds any.</param>
    /// <returns>Whether it is a markup value.</returns>
    private bool IsMarkupValue(JsonElement element, out string markup)
    {
        markup = string.Empty;

        if (!element.TryGetProperty(ContentPayloadMembers.Type, out var type) ||
            type.ValueKind is not JsonValueKind.String ||
            type.GetString() is not { Length: > 0 } key)
        {
            return false;
        }

        // A key nothing is registered under contributes nothing, the way it does everywhere else a
        // payload is walked: content outlives the code deployed when it was written.
        if (fieldTypes.Find(key) is not { } fieldType ||
            !fieldType.Capabilities.HasFlag(FieldTypeCapabilities.Sanitizable))
        {
            return false;
        }

        if (element.TryGetProperty("value", out var value) && value.ValueKind is JsonValueKind.String)
        {
            markup = value.GetString() ?? string.Empty;
        }

        return true;
    }

    /// <summary>An element's text, collapsed and trimmed, for a message an editor has to recognise.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The text, truncated so a whole paragraph cannot end up in a diagnostic.</returns>
    private static string Text(IElement element)
    {
        var text = string.Join(' ', element.TextContent.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return text.Length <= 60 ? text : text[..57] + "…";
    }

    /// <summary>Reduces link text to the form the list is written in.</summary>
    /// <param name="text">The link's text.</param>
    /// <returns>The comparison form.</returns>
    private static string Normalize(string text) =>
        text.Trim().TrimEnd('.', '!', '>', '›', '→', ' ').ToLowerInvariant();

    /// <summary>Whether the link's text is the address rather than a description of it.</summary>
    /// <param name="text">The link's text.</param>
    /// <returns>Whether it reads as a URL.</returns>
    private static bool IsBareUrl(string text) =>
        text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
}
