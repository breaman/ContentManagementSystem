using System.Text.Json;

using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Renders one zone of the page being delivered (spec section 15.2).
/// </summary>
/// <remarks>
/// The indirection the whole content model rests on: a template names a zone key, this reads that
/// zone's stored value, resolves the renderer for the field type <em>the value itself declares</em>,
/// and renders it. The template never learns what field type its zones hold, so changing a zone from
/// rich text to a block list is a backoffice edit rather than a code change.
/// <para>
/// Four conditions render nothing, and every one of them is ordinary rather than exceptional
/// (spec section 15.3):
/// </para>
/// <list type="bullet">
/// <item>the zone was never authored — a template may gain a zone long before any page fills it,
/// which is what makes "adding a zone is free" true at render time as well as at validation
/// time;</item>
/// <item>an editor cleared it;</item>
/// <item>the stored value carries no field type discriminator, which is logged, because it means
/// something wrote a payload this build cannot read;</item>
/// <item>no renderer is registered for the field type it names, which is logged — the field type
/// was removed from the deployment while content authored against it is still live.</item>
/// </list>
/// </remarks>
public partial class CmsZone : ComponentBase
{
    /// <summary>The zone key, as declared by the template and stored in the payload.</summary>
    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = string.Empty;

    /// <summary>The render context, cascaded by the delivery host.</summary>
    [CascadingParameter]
    public RenderContext Context { get; set; } = default!;

    [Inject]
    private IFieldRendererCatalog Renderers { get; set; } = default!;

    [Inject]
    private ILogger<CmsZone> Logger { get; set; } = default!;

    private Type? RendererType { get; set; }

    private Dictionary<string, object?> RendererParameters { get; set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        RendererType = null;
        RendererParameters = [];

        if (Context is null)
        {
            // Only reachable from a template rendered outside the delivery host. Logged rather than
            // thrown for the same reason as everything else here: a misconfigured render must not
            // take a page down.
            Logger.LogWarning(
                "Zone '{ZoneKey}' rendered with no cascading {ContextType}.",
                Name,
                nameof(RenderContext));

            return;
        }

        // Absent and cleared are different facts about the content, and neither renders: nothing was
        // ever authored, or an editor deliberately emptied it.
        if (Context.Payload.GetZoneState(Name) is not ContentValueState.Present)
        {
            return;
        }

        var value = Context.Payload.GetZone(Name);

        if (!TryGetFieldTypeKey(value, out var fieldTypeKey))
        {
            Logger.LogWarning(
                "Zone '{ZoneKey}' on page {PageId} version {VersionId} stores no '{Member}' " +
                "discriminator, so nothing can say how to read it.",
                Name,
                Context.Page.Id,
                Context.Page.VersionId,
                ContentPayloadMembers.Type);

            return;
        }

        if (!Renderers.TryGetRenderer(fieldTypeKey, out var renderer))
        {
            Logger.LogWarning(
                "No renderer is registered for field type '{FieldTypeKey}' (zone '{ZoneKey}', " +
                "page {PageId}, version {VersionId}); the zone renders nothing.",
                fieldTypeKey,
                Name,
                Context.Page.Id,
                Context.Page.VersionId);

            return;
        }

        RendererType = renderer;
        RendererParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [nameof(CmsFieldRendererBase.Value)] = value,
            [nameof(CmsFieldRendererBase.PropertyKey)] = Name,
            [nameof(CmsFieldRendererBase.Configuration)] = Configuration(fieldTypeKey),
        };
    }

    private static bool TryGetFieldTypeKey(JsonElement value, out string fieldTypeKey)
    {
        fieldTypeKey = string.Empty;

        if (value.ValueKind is not JsonValueKind.Object ||
            !value.TryGetProperty(ContentPayloadMembers.Type, out var type) ||
            type.ValueKind is not JsonValueKind.String)
        {
            return false;
        }

        fieldTypeKey = type.GetString() ?? string.Empty;

        return fieldTypeKey.Length > 0;
    }

    /// <summary>
    /// The configuration captured for this zone, but only when the schema agrees with the value
    /// about what field type it is.
    /// </summary>
    /// <remarks>
    /// The two disagree when a zone's field type was changed under stored content. Handing a
    /// renderer the configuration of a different field type is worse than handing it none: the
    /// settings would parse, and it would render with bounds and formats meant for something else.
    /// </remarks>
    private FieldConfiguration Configuration(string fieldTypeKey) =>
        Context.Schema?.FindZone(Name) is { } zone &&
        string.Equals(zone.FieldTypeKey, fieldTypeKey, StringComparison.Ordinal)
            ? zone.Configuration
            : FieldConfiguration.Empty;
}
