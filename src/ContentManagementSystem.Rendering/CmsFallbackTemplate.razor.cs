using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Content;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// What a page renders as when no deployed component declares its template key
/// (spec section 15.3, task P3-11).
/// </summary>
/// <remarks>
/// The first row of the fallback matrix. A template component can go missing for entirely ordinary
/// reasons — a deployment rolled back, an environment promoted out of order, a developer deleting a
/// component while pages built on it are still live — and the site's answer has to be a page that
/// still says what it was about. A blank response and a stack trace are both worse than plain text.
/// <para>
/// <strong>The text is asked of the field types, not read out of the JSON here.</strong> Each zone's
/// value is dispatched to the field type its stored <c>type</c> discriminator names, and reduced
/// through <c>ExtractSearchText</c> — the same method the search index is built from. That is the
/// only way to get readable text out of a payload whose shapes are runtime data: a walk written here
/// would have to know that rich text hides its words inside markup, that a block list nests them two
/// levels down, and that a media reference has none, and it would be wrong about the next field type
/// somebody adds.
/// </para>
/// <para>
/// A zone whose field type this deployment no longer registers contributes nothing rather than its
/// raw JSON. The page has already lost its template; putting a serialized payload on the public site
/// on top of that is not a recovery.
/// </para>
/// </remarks>
public partial class CmsFallbackTemplate : CmsTemplateBase
{
    [Inject]
    private IFieldTypeRegistry FieldTypes { get; set; } = default!;

    [Inject]
    private ILogger<CmsFallbackTemplate> Logger { get; set; } = default!;

    /// <summary>The page's readable text, one entry per zone that had any.</summary>
    protected IReadOnlyList<string> Paragraphs { get; private set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var paragraphs = new List<string>();

        // Payload order rather than schema order, because there may be no schema: the template
        // revision this content was authored against is exactly as likely to have gone missing as
        // the component, and it is not needed to read a value whose writer is named on the value.
        foreach (var zoneKey in Context.Payload.ZoneKeys)
        {
            if (Text(zoneKey, Context.Payload.GetZone(zoneKey)) is { Length: > 0 } text)
            {
                paragraphs.Add(text);
            }
        }

        Paragraphs = paragraphs;
    }

    private string? Text(string zoneKey, JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Object ||
            !value.TryGetProperty(ContentPayloadMembers.Type, out var type) ||
            type.ValueKind is not JsonValueKind.String ||
            type.GetString() is not { Length: > 0 } fieldTypeKey ||
            FieldTypes.Find(fieldTypeKey) is not { } fieldType)
        {
            return null;
        }

        // A field type's own failure must not become the second failure of the request: this
        // component is already what the page has left, and there is no boundary below it to catch
        // anything. Caught per zone, so one bad value costs one paragraph rather than the page.
        try
        {
            var text = fieldType.ExtractSearchText(value);

            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        catch (Exception exception)
        {
            Logger.LogWarning(
                exception,
                "Extracting fallback text from zone '{ZoneKey}' ('{FieldTypeKey}') on page {PageId} " +
                "version {VersionId} failed; the zone contributes nothing and the fallback still renders.",
                zoneKey,
                fieldTypeKey,
                Page.Id,
                Page.VersionId);

            return null;
        }
    }
}
