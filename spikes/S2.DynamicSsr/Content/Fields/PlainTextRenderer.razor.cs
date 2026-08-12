using System.Text.Json;
using S2.DynamicSsr.Cms;

namespace S2.DynamicSsr.Content.Fields;

/// <summary>Renders a <c>plainText</c> value. No HTML is permitted, so the content is encoded.</summary>
public partial class PlainTextRenderer : CmsFieldRendererBase
{
    private string Content =>
        Value.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;
}
