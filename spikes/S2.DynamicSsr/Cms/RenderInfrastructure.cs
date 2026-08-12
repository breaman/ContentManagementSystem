using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace S2.DynamicSsr.Cms;

/// <summary>
/// Marks a Razor component as a CMS template. <see cref="TemplateRegistry"/> discovers these at
/// startup, standing in for <c>TemplateReconciler</c> (task P1-25).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CmsTemplateAttribute(string key, string name) : Attribute
{
    public string Key { get; } = key;

    public string Name { get; } = name;
}

/// <summary>Marks a Razor component as a CMS block type.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CmsBlockTypeAttribute(string key, string name) : Attribute
{
    public string Key { get; } = key;

    public string Name { get; } = name;
}

public enum CmsRenderMode
{
    Live,
    Preview,
}

/// <summary>
/// Spec §15.2. Note the name: the spec calls this <c>RenderMode</c>, which collides with
/// <see cref="Microsoft.AspNetCore.Components.Web.RenderMode"/> inside every .razor file that imports
/// the Web namespace. Renamed here, and P3-08 should do the same.
/// </summary>
public sealed record CmsRenderContext(
    long PageId,
    long VersionId,
    string TemplateKey,
    JsonElement Payload,
    CmsRenderMode Mode,
    ISet<string> CacheTags)
{
    public bool TryGetZone(string key, out JsonElement zone)
    {
        zone = default;

        return Payload.ValueKind == JsonValueKind.Object &&
               Payload.TryGetProperty("zones", out var zones) &&
               zones.ValueKind == JsonValueKind.Object &&
               zones.TryGetProperty(key, out zone) &&
               zone.ValueKind == JsonValueKind.Object;
    }

    /// <summary>Text fallback for the unknown-template case in spec §15.3.</summary>
    public IEnumerable<string> TextContent()
    {
        if (Payload.ValueKind != JsonValueKind.Object || !Payload.TryGetProperty("zones", out var zones))
        {
            yield break;
        }

        foreach (var text in Walk(zones))
        {
            yield return text;
        }

        static IEnumerable<string> Walk(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("value") && property.Value.ValueKind == JsonValueKind.String)
                        {
                            yield return property.Value.GetString()!;
                        }
                        else
                        {
                            foreach (var text in Walk(property.Value))
                            {
                                yield return text;
                            }
                        }
                    }

                    break;

                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                    {
                        foreach (var text in Walk(item))
                        {
                            yield return text;
                        }
                    }

                    break;
            }
        }
    }
}

/// <summary>Base for template components — spec §8.2.</summary>
public abstract class CmsTemplateBase : ComponentBase
{
    [CascadingParameter]
    public CmsRenderContext Context { get; set; } = default!;
}

/// <summary>Base for block components. Properties arrive as raw JSON, per the runtime schema.</summary>
public abstract class CmsBlockBase : ComponentBase
{
    [Parameter]
    public JsonElement Properties { get; set; }

    [CascadingParameter]
    public CmsRenderContext Context { get; set; } = default!;

    protected string Text(string propertyKey) =>
        Properties.ValueKind == JsonValueKind.Object &&
        Properties.TryGetProperty(propertyKey, out var property) &&
        property.ValueKind == JsonValueKind.Object &&
        property.TryGetProperty("value", out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;

    protected JsonElement? Property(string propertyKey) =>
        Properties.ValueKind == JsonValueKind.Object && Properties.TryGetProperty(propertyKey, out var property)
            ? property
            : null;
}

/// <summary>Base for field renderer components — one per field type, spec §7.</summary>
public abstract class CmsFieldRendererBase : ComponentBase
{
    [Parameter]
    public JsonElement Value { get; set; }

    [Parameter]
    public string ZoneKey { get; set; } = string.Empty;

    [CascadingParameter]
    public CmsRenderContext Context { get; set; } = default!;
}

public sealed class TemplateRegistry
{
    private readonly Dictionary<string, Type> _byKey = new(StringComparer.Ordinal);

    public TemplateRegistry(params Assembly[] assemblies)
    {
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            if (type.GetCustomAttribute<CmsTemplateAttribute>() is { } attribute)
            {
                _byKey[attribute.Key] = type;
            }
        }
    }

    public IReadOnlyCollection<string> Keys => _byKey.Keys;

    public bool TryResolve(string key, out Type component) => _byKey.TryGetValue(key, out component!);
}

public sealed class BlockTypeRegistry
{
    private readonly Dictionary<string, Type> _byKey = new(StringComparer.Ordinal);

    public BlockTypeRegistry(params Assembly[] assemblies)
    {
        foreach (var type in assemblies.SelectMany(a => a.GetTypes()))
        {
            if (type.GetCustomAttribute<CmsBlockTypeAttribute>() is { } attribute)
            {
                _byKey[attribute.Key] = type;
            }
        }
    }

    public bool TryResolve(string key, out Type component) => _byKey.TryGetValue(key, out component!);
}

/// <summary>Field type key → renderer component. The registry is data; the renderer is discovered, not switched on.</summary>
public sealed class FieldRendererRegistry
{
    private readonly Dictionary<string, Type> _byKey;

    public FieldRendererRegistry(IReadOnlyDictionary<string, Type> renderers) =>
        _byKey = new Dictionary<string, Type>(renderers, StringComparer.Ordinal);

    public bool TryResolve(string fieldTypeKey, out Type component) => _byKey.TryGetValue(fieldTypeKey, out component!);
}

/// <summary>Records what the render pipeline logged, so the harness can assert on it.</summary>
public sealed class RenderDiagnostics
{
    private readonly List<string> _entries = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<string> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    public void Record(string entry)
    {
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
