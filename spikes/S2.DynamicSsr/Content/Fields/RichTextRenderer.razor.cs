using System.Net;
using System.Text.Json;
using S2.DynamicSsr.Cms;

namespace S2.DynamicSsr.Content.Fields;

/// <summary>Renders a <c>richText</c> value as pre-sanitized markup.</summary>
public partial class RichTextRenderer : CmsFieldRendererBase
{
    private string Html
    {
        get
        {
            if (!Value.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            var raw = value.GetString()!;

            return Value.TryGetProperty("format", out var format) && format.ValueEquals("markdown")
                ? $"<p>{WebUtility.HtmlEncode(raw)}</p>"
                : raw;
        }
    }
}
