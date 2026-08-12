namespace S1.RuntimeSchema;

// The schema is DATA, not CLR types. In the real system these come out of the Template /
// TemplateRevision / Zone / BlockType / BlockTypeRevision / BlockTypeProperty tables (spec §23.1).
// The spike loads them from a JSON file to prove the validator never needs a compile-time POCO.

public sealed record TemplateRevisionSchema(
    string TemplateKey,
    int RevisionNumber,
    IReadOnlyList<ZoneSchema> Zones)
{
    private Dictionary<string, ZoneSchema>? _byKey;

    public bool TryGetZone(string key, out ZoneSchema zone)
    {
        _byKey ??= Zones.ToDictionary(z => z.Key, StringComparer.Ordinal);
        return _byKey.TryGetValue(key, out zone!);
    }
}

public sealed record ZoneSchema(
    string Key,
    string Name,
    string FieldTypeKey,
    bool IsRequired,
    int SortOrder,
    string? ConfigurationJson);

public sealed record BlockTypeRevisionSchema(
    string BlockTypeKey,
    int RevisionNumber,
    IReadOnlyList<PropertySchema> Properties)
{
    private Dictionary<string, PropertySchema>? _byKey;

    public bool TryGetProperty(string key, out PropertySchema property)
    {
        _byKey ??= Properties.ToDictionary(p => p.Key, StringComparer.Ordinal);
        return _byKey.TryGetValue(key, out property!);
    }
}

public sealed record PropertySchema(
    string Key,
    string Name,
    string FieldTypeKey,
    bool IsRequired,
    int SortOrder,
    string? ConfigurationJson);

/// <summary>
/// Stands in for the structure tables. Revisions are keyed by (key, revisionNumber) because a
/// published payload names the revision it was authored against — spec §8.5.
/// </summary>
public sealed class SchemaCatalog
{
    private readonly Dictionary<(string Key, int Revision), TemplateRevisionSchema> _templates = new();
    private readonly Dictionary<(string Key, int Revision), BlockTypeRevisionSchema> _blockTypes = new();

    public void Add(TemplateRevisionSchema template) =>
        _templates[(template.TemplateKey, template.RevisionNumber)] = template;

    public void Add(BlockTypeRevisionSchema blockType) =>
        _blockTypes[(blockType.BlockTypeKey, blockType.RevisionNumber)] = blockType;

    public bool TryGetTemplate(string key, int revision, out TemplateRevisionSchema template) =>
        _templates.TryGetValue((key, revision), out template!);

    public bool TryGetBlockType(string key, int revision, out BlockTypeRevisionSchema blockType) =>
        _blockTypes.TryGetValue((key, revision), out blockType!);
}
