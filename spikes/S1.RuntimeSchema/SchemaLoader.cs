using System.Text.Json;

namespace S1.RuntimeSchema;

public static class SchemaLoader
{
    public static SchemaCatalog Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var catalog = new SchemaCatalog();

        foreach (var template in root.GetProperty("templates").EnumerateArray())
        {
            var zones = template.GetProperty("zones").EnumerateArray()
                .Select(z => new ZoneSchema(
                    z.GetProperty("key").GetString()!,
                    z.GetProperty("name").GetString()!,
                    z.GetProperty("fieldTypeKey").GetString()!,
                    z.GetProperty("isRequired").GetBoolean(),
                    z.GetProperty("sortOrder").GetInt32(),
                    RawConfiguration(z)))
                .OrderBy(z => z.SortOrder)
                .ToList();

            catalog.Add(new TemplateRevisionSchema(
                template.GetProperty("templateKey").GetString()!,
                template.GetProperty("revisionNumber").GetInt32(),
                zones));
        }

        foreach (var blockType in root.GetProperty("blockTypes").EnumerateArray())
        {
            var properties = blockType.GetProperty("properties").EnumerateArray()
                .Select(p => new PropertySchema(
                    p.GetProperty("key").GetString()!,
                    p.GetProperty("name").GetString()!,
                    p.GetProperty("fieldTypeKey").GetString()!,
                    p.GetProperty("isRequired").GetBoolean(),
                    p.GetProperty("sortOrder").GetInt32(),
                    RawConfiguration(p)))
                .OrderBy(p => p.SortOrder)
                .ToList();

            catalog.Add(new BlockTypeRevisionSchema(
                blockType.GetProperty("blockTypeKey").GetString()!,
                blockType.GetProperty("revisionNumber").GetInt32(),
                properties));
        }

        return catalog;
    }

    private static string? RawConfiguration(in JsonElement owner) =>
        owner.TryGetProperty("configuration", out var config) && config.ValueKind == JsonValueKind.Object
            ? config.GetRawText()
            : null;
}
