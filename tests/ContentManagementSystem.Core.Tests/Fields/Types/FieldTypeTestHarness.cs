using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// Shared plumbing for the built-in field type tests (task P1-10).
/// </summary>
/// <remarks>
/// Values are written as JSON text rather than built with a fluent helper on purpose: what is being
/// asserted is behaviour against the payload shape in spec section 6.2, and a test that spells that
/// shape out fails loudly if the shape ever changes.
/// </remarks>
internal static class FieldTypeTestHarness
{
    /// <summary>Parses JSON text into a detached element.</summary>
    /// <param name="json">The JSON text.</param>
    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.Clone();
    }

    /// <summary>An element standing for a property that was never authored.</summary>
    public static JsonElement Absent => default;

    /// <summary>Validates a stored property written as JSON text.</summary>
    /// <param name="fieldType">The field type under test.</param>
    /// <param name="propertyJson">The stored property object.</param>
    /// <param name="configurationJson">The property's configuration, if any.</param>
    /// <param name="mode">Draft or publish.</param>
    public static ValueTask<ValidationResult> ValidateAsync(
        this IFieldType fieldType,
        string propertyJson,
        string? configurationJson = null,
        ValidationMode mode = ValidationMode.Draft) =>
        fieldType.ValidateAsync(
            Element(propertyJson),
            FieldConfiguration.Parse(configurationJson),
            mode,
            TestContext.Current.CancellationToken);

    /// <summary>Validates an element directly, for the absent and null cases.</summary>
    /// <param name="fieldType">The field type under test.</param>
    /// <param name="property">The stored property.</param>
    /// <param name="configurationJson">The property's configuration, if any.</param>
    /// <param name="mode">Draft or publish.</param>
    public static ValueTask<ValidationResult> ValidateAsync(
        this IFieldType fieldType,
        JsonElement property,
        string? configurationJson = null,
        ValidationMode mode = ValidationMode.Draft) =>
        fieldType.ValidateAsync(
            property,
            FieldConfiguration.Parse(configurationJson),
            mode,
            TestContext.Current.CancellationToken);

    /// <summary>Sanitizes a stored property written as JSON text.</summary>
    /// <param name="fieldType">The field type under test.</param>
    /// <param name="propertyJson">The stored property object.</param>
    /// <param name="configurationJson">The property's configuration, if any.</param>
    public static ValueTask<JsonElement> SanitizeAsync(
        this IFieldType fieldType,
        string propertyJson,
        string? configurationJson = null) =>
        fieldType.SanitizeAsync(
            Element(propertyJson),
            FieldConfiguration.Parse(configurationJson),
            TestContext.Current.CancellationToken);

    /// <summary>The codes reported, for asserting on without pinning message wording.</summary>
    /// <param name="result">The validation result.</param>
    public static IEnumerable<string> Codes(this ValidationResult result) =>
        result.Diagnostics.Select(diagnostic => diagnostic.Code);
}

/// <summary>
/// An <see cref="IContentSanitizer"/> that records what it was asked to clean and applies whatever
/// transformation a test needs.
/// </summary>
/// <remarks>
/// The real implementation lands in P1-18. What matters here is the field type's half of the
/// contract: which profile it selects, whether it calls the sanitizer at all, and what it does with
/// the answer.
/// </remarks>
internal sealed class RecordingSanitizer : IContentSanitizer
{
    /// <summary>Every call made, in order.</summary>
    public List<(string? Html, SanitizationProfile Profile)> Calls { get; } = [];

    /// <summary>What to return. The default is a pass-through.</summary>
    public Func<string?, string> Transform { get; set; } = html => html ?? string.Empty;

    /// <inheritdoc />
    public string Sanitize(string? html, SanitizationProfile profile)
    {
        Calls.Add((html, profile));

        return Transform(html);
    }
}
