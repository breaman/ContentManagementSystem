using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace S2.DynamicSsr.Cms;

/// <summary>
/// Reads one zone's value out of the payload, resolves the renderer for its field type, and renders
/// it inside an error boundary. The template never knows which field type a zone holds.
/// </summary>
public partial class CmsZone : ComponentBase
{
    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = string.Empty;

    [CascadingParameter]
    public CmsRenderContext Context { get; set; } = default!;

    [Inject]
    private FieldRendererRegistry Renderers { get; set; } = default!;

    [Inject]
    private RenderDiagnostics Diagnostics { get; set; } = default!;

    [Inject]
    private ILogger<CmsZone> Logger { get; set; } = default!;

    private Type? RendererType { get; set; }

    private Dictionary<string, object?> RendererParameters { get; set; } = [];

    protected override void OnParametersSet()
    {
        RendererType = null;

        // An absent zone renders empty — a template can gain a zone before any page fills it.
        if (!Context.TryGetZone(Name, out var zone))
        {
            return;
        }

        if (!zone.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String)
        {
            Logger.LogWarning("Zone '{ZoneKey}' on page {PageId} has no field type discriminator.", Name, Context.PageId);
            Diagnostics.Record($"zone.type.missing zone={Name} page={Context.PageId}");

            return;
        }

        var fieldTypeKey = type.GetString()!;

        // Spec §15.3: an unknown field type key renders nothing, logs a warning, and never throws.
        if (!Renderers.TryResolve(fieldTypeKey, out var renderer))
        {
            Logger.LogWarning(
                "No renderer is registered for field type '{FieldTypeKey}' (zone '{ZoneKey}', page {PageId}).",
                fieldTypeKey,
                Name,
                Context.PageId);
            Diagnostics.Record($"fieldType.unknown key={fieldTypeKey} zone={Name} page={Context.PageId}");

            return;
        }

        RendererType = renderer;
        RendererParameters = new Dictionary<string, object?>
        {
            ["Value"] = zone,
            ["ZoneKey"] = Name,
        };
    }
}
