using System.Text.Json;
using System.Text.RegularExpressions;

namespace S1.RuntimeSchema;

public enum Severity { Error, Warning }

public sealed record Diagnostic(Severity Severity, string Path, string Code, string Message);

public sealed record ContentReference(string TargetType, long TargetId, string Path);

/// <summary>Pared-down <c>IFieldType</c> (spec §7) — enough of the contract to prove the dispatch.</summary>
public interface IFieldType
{
    string Key { get; }

    void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx);

    void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into);
}

public readonly struct FieldConfiguration
{
    public FieldConfiguration(JsonElement? root) => Root = root;

    public JsonElement? Root { get; }

    public int? GetInt32(string name) =>
        Root is { } r && r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32()
            : null;

    public string? GetString(string name) =>
        Root is { } r && r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    public bool GetBoolean(string name, bool fallback) =>
        Root is { } r && r.TryGetProperty(name, out var p) && (p.ValueKind is JsonValueKind.True or JsonValueKind.False)
            ? p.GetBoolean()
            : fallback;

    public JsonElement.ArrayEnumerator? GetArray(string name) =>
        Root is { } r && r.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Array
            ? p.EnumerateArray()
            : null;

    public bool ArrayContains(string name, string candidate)
    {
        if (GetArray(name) is not { } items)
        {
            return true; // no constraint configured
        }

        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), candidate, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Carries the diagnostics list and the current payload path. The path is a stack of segments so
/// nothing is concatenated on the happy path — string building only happens when a diagnostic is
/// actually produced. This is the difference between a validator that costs microseconds and one
/// that allocates a string per property.
/// </summary>
public sealed class ValidationContext
{
    private readonly List<string> _segments = new(8);

    public ValidationContext(SchemaCatalog catalog, ValidationMode mode)
    {
        Catalog = catalog;
        Mode = mode;
    }

    public SchemaCatalog Catalog { get; }

    public ValidationMode Mode { get; }

    public List<Diagnostic> Diagnostics { get; } = new();

    public bool HasErrors => Diagnostics.Any(d => d.Severity == Severity.Error);

    public IDisposable Push(string segment)
    {
        _segments.Add(segment);
        return new PopScope(this);
    }

    public string CurrentPath() => string.Join('.', _segments).Replace(".[", "[", StringComparison.Ordinal);

    public void Error(string code, string message) =>
        Diagnostics.Add(new Diagnostic(Severity.Error, CurrentPath(), code, message));

    public void Warn(string code, string message) =>
        Diagnostics.Add(new Diagnostic(Severity.Warning, CurrentPath(), code, message));

    public void Reset() => Diagnostics.Clear();

    private sealed class PopScope(ValidationContext owner) : IDisposable
    {
        public void Dispose() => owner._segments.RemoveAt(owner._segments.Count - 1);
    }
}

public enum ValidationMode
{
    /// <summary>Draft save: shape must be valid, but required-but-empty is only a warning (spec §8.3).</summary>
    Draft,

    /// <summary>Publish: required zones and properties must be filled (spec §14.6).</summary>
    Publish,
}

public sealed class FieldTypeRegistry
{
    private readonly Dictionary<string, IFieldType> _byKey;

    public FieldTypeRegistry(IEnumerable<IFieldType> fieldTypes) =>
        _byKey = fieldTypes.ToDictionary(f => f.Key, StringComparer.Ordinal);

    public bool TryGet(string key, out IFieldType fieldType) => _byKey.TryGetValue(key, out fieldType!);

    public IReadOnlyCollection<string> Keys => _byKey.Keys;
}

// ---------------------------------------------------------------------------------------------
// Value field types
// ---------------------------------------------------------------------------------------------

public sealed class PlainTextFieldType : IFieldType
{
    public string Key => "plainText";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("value", out var v))
        {
            ctx.Error("field.value.missing", "Expected a 'value' member.");
            return;
        }

        if (v.ValueKind == JsonValueKind.Null)
        {
            return; // explicitly cleared — distinct from absent, spec §6.2
        }

        if (v.ValueKind != JsonValueKind.String)
        {
            ctx.Error("field.type.mismatch", $"Expected a string, found {v.ValueKind}.");
            return;
        }

        var text = v.GetString()!;

        if (config.GetInt32("maxLength") is { } max && text.Length > max)
        {
            ctx.Error("field.maxLength", $"Value is {text.Length} characters; the maximum is {max}.");
        }

        if (config.GetString("pattern") is { } pattern &&
            !Regex.IsMatch(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(50)))
        {
            ctx.Error("field.pattern", $"Value does not match the required pattern '{pattern}'.");
        }

        if (text.Contains('<', StringComparison.Ordinal))
        {
            ctx.Warn("field.plainText.markup", "Markup will be HTML-encoded on render; plainText stores no HTML.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into) { }
}

public sealed class RichTextFieldType : IFieldType
{
    private static readonly string[] Formats = ["markdown", "html"];

    public string Key => "richText";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String)
        {
            ctx.Error("field.richText.format.missing", "Expected 'format' to be \"markdown\" or \"html\".");
            return;
        }

        var formatValue = format.GetString()!;
        if (Array.IndexOf(Formats, formatValue) < 0)
        {
            ctx.Error("field.richText.format.unknown", $"Unknown format '{formatValue}'.");
        }

        if (config.GetString("allowedFormat") is { } required && !string.Equals(required, formatValue, StringComparison.Ordinal))
        {
            ctx.Error("field.richText.format.notAllowed", $"This zone is configured for '{required}' only.");
        }

        if (!value.TryGetProperty("value", out var v) || (v.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)))
        {
            ctx.Error("field.type.mismatch", "Expected 'value' to be a string or null.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into) { }
}

public sealed class NumberFieldType : IFieldType
{
    public string Key => "number";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("value", out var v))
        {
            ctx.Error("field.value.missing", "Expected a 'value' member.");
            return;
        }

        if (v.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (v.ValueKind != JsonValueKind.Number)
        {
            ctx.Error("field.type.mismatch", $"Expected a number, found {v.ValueKind}.");
            return;
        }

        var number = v.GetDecimal();
        if (config.GetInt32("min") is { } min && number < min)
        {
            ctx.Error("field.min", $"Value {number} is below the minimum {min}.");
        }

        if (config.GetInt32("max") is { } max && number > max)
        {
            ctx.Error("field.max", $"Value {number} is above the maximum {max}.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into) { }
}

public sealed class BooleanFieldType : IFieldType
{
    public string Key => "boolean";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("value", out var v) ||
            v.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
        {
            ctx.Error("field.type.mismatch", "Expected 'value' to be a boolean or null.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into) { }
}

public sealed class ChoiceFieldType : IFieldType
{
    public string Key => "choice";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("value", out var v))
        {
            ctx.Error("field.value.missing", "Expected a 'value' member.");
            return;
        }

        if (v.ValueKind is JsonValueKind.Null)
        {
            return;
        }

        if (v.ValueKind != JsonValueKind.String)
        {
            ctx.Error("field.type.mismatch", $"Expected a string, found {v.ValueKind}.");
            return;
        }

        if (!config.ArrayContains("options", v.GetString()!))
        {
            ctx.Error("field.choice.unknownOption", $"'{v.GetString()}' is not one of the configured options.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into) { }
}

// ---------------------------------------------------------------------------------------------
// Reference-bearing field types — the ones that must never under-report (spec §7.3)
// ---------------------------------------------------------------------------------------------

public sealed class MediaFieldType : IFieldType
{
    public string Key => "media";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("mediaId", out var id))
        {
            ctx.Error("field.media.missing", "Expected a 'mediaId' member.");
            return;
        }

        if (id.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (id.ValueKind != JsonValueKind.Number)
        {
            ctx.Error("field.type.mismatch", $"Expected 'mediaId' to be a number, found {id.ValueKind}.");
            return;
        }

        if (value.TryGetProperty("focalPoint", out var focal) && focal.ValueKind == JsonValueKind.Object)
        {
            foreach (var axis in (ReadOnlySpan<string>)["x", "y"])
            {
                if (!focal.TryGetProperty(axis, out var a) || a.ValueKind != JsonValueKind.Number ||
                    a.GetDouble() is < 0 or > 1)
                {
                    ctx.Error("field.media.focalPoint", $"focalPoint.{axis} must be a number in [0,1].");
                }
            }
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into)
    {
        if (value.TryGetProperty("mediaId", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            into.Add(new ContentReference("Media", id.GetInt64(), ctx.CurrentPath()));
        }
    }
}

public sealed class LinkFieldType : IFieldType
{
    private static readonly string[] Kinds = ["page", "external", "media", "anchor", "email"];

    public string Key => "link";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
        {
            ctx.Error("field.link.kind.missing", "Expected 'kind' to be one of page|external|media|anchor|email.");
            return;
        }

        var kindValue = kind.GetString()!;
        if (Array.IndexOf(Kinds, kindValue) < 0)
        {
            ctx.Error("field.link.kind.unknown", $"Unknown link kind '{kindValue}'.");
            return;
        }

        switch (kindValue)
        {
            case "page" when !HasNumber(value, "pageId"):
                ctx.Error("field.link.pageId", "An internal link must carry 'pageId'; URLs are resolved at render (ADR-0006).");
                break;
            case "media" when !HasNumber(value, "mediaId"):
                ctx.Error("field.link.mediaId", "A media link must carry 'mediaId'.");
                break;
            case "external" when !HasString(value, "url"):
                ctx.Error("field.link.url", "An external link must carry 'url'.");
                break;
        }

        if (value.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String &&
            url.GetString() is { } raw && raw.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Error("field.link.scheme", "Scheme not allowed.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into)
    {
        if (value.TryGetProperty("pageId", out var pageId) && pageId.ValueKind == JsonValueKind.Number)
        {
            into.Add(new ContentReference("Page", pageId.GetInt64(), ctx.CurrentPath()));
        }

        if (value.TryGetProperty("mediaId", out var mediaId) && mediaId.ValueKind == JsonValueKind.Number)
        {
            into.Add(new ContentReference("Media", mediaId.GetInt64(), ctx.CurrentPath()));
        }
    }

    private static bool HasNumber(in JsonElement value, string name) =>
        value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number;

    private static bool HasString(in JsonElement value, string name) =>
        value.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String;
}

public sealed class ReusableFieldType : IFieldType
{
    public string Key => "reusable";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx)
    {
        if (!value.TryGetProperty("reusableContentId", out var id) ||
            (id.ValueKind is not (JsonValueKind.Number or JsonValueKind.Null)))
        {
            ctx.Error("field.reusable.id", "Expected 'reusableContentId' to be a number or null.");
        }
    }

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into)
    {
        if (value.TryGetProperty("reusableContentId", out var id) && id.ValueKind == JsonValueKind.Number)
        {
            into.Add(new ContentReference("ReusableContent", id.GetInt64(), ctx.CurrentPath()));
        }
    }
}

/// <summary>
/// The interesting one: <c>blocks</c> recurses back into the schema walk, so a block property can
/// itself be a block list. The depth guard is the spike's answer to "one level of nesting in v1".
/// </summary>
public sealed class BlocksFieldType(PayloadValidator validator) : IFieldType
{
    public string Key => "blocks";

    public void Validate(in JsonElement value, in FieldConfiguration config, ValidationContext ctx) =>
        validator.ValidateBlockList(value, config, ctx);

    public void ExtractReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into) =>
        validator.ExtractBlockListReferences(value, ctx, into);
}
