using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Core.Delivery.Seo;

/// <summary>
/// Serializes the structured-data documents the head emits (spec section 18.2).
/// </summary>
/// <remarks>
/// JSON-LD goes inside a <c>&lt;script&gt;</c> element, which is the one place in an HTML document
/// where ordinary HTML escaping does not apply — a script body ends at the first literal
/// <c>&lt;/script</c> whatever surrounds it. That is why everything here goes through
/// <see cref="JsonSerializer"/> with the default encoder, which escapes <c>&lt;</c>, <c>&gt;</c>,
/// and <c>&amp;</c> as <c>\u003C</c> and friends: a page title containing markup cannot terminate
/// the script, so the block cannot become an injection point.
/// <para>
/// The same treatment is given to the editor's own <c>StructuredDataJson</c>. It is validated as
/// well-formed JSON when it is saved, which says nothing about what it contains; re-parsing and
/// re-serializing it here is what makes a hand-authored document as safe as a generated one.
/// </para>
/// </remarks>
internal static class JsonLd
{
    /// <summary>The schema.org context every document declares.</summary>
    public const string Context = "https://schema.org";

    private static readonly JsonSerializerOptions Options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Serializes one document.</summary>
    /// <param name="document">The property bag, with <c>@context</c> and <c>@type</c> already set.</param>
    /// <returns>The JSON text to place in the script element.</returns>
    public static string Serialize(IDictionary<string, object?> document) =>
        JsonSerializer.Serialize(document, Options);

    /// <summary>
    /// Re-serializes an editor's hand-authored JSON-LD, or returns null when it is unusable.
    /// </summary>
    /// <param name="authored">The stored text.</param>
    /// <returns>The normalized text, or null when it is blank or not well-formed.</returns>
    /// <remarks>
    /// Malformed JSON is dropped rather than emitted or thrown on. A page is not worth failing to
    /// serve over its structured data, and emitting text that does not parse would put an error in
    /// front of every crawler that reads the page.
    /// </remarks>
    public static string? Normalize(string? authored)
    {
        if (string.IsNullOrWhiteSpace(authored)) return null;

        try
        {
            using var document = JsonDocument.Parse(authored);

            return JsonSerializer.Serialize(document.RootElement, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Starts a document of a given schema.org type.</summary>
    /// <param name="type">The <c>@type</c>, such as <c>WebPage</c>.</param>
    /// <returns>The bag, ready for its properties.</returns>
    public static Dictionary<string, object?> Document(string type) => new(StringComparer.Ordinal)
    {
        ["@context"] = Context,
        ["@type"] = type,
    };
}
