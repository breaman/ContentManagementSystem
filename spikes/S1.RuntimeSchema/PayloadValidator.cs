using System.Collections.Concurrent;
using System.Text.Json;

namespace S1.RuntimeSchema;

/// <summary>
/// Stand-in for <c>ContentSchemaValidator</c> (task P1-15). Walks a payload against a
/// <em>runtime</em> schema and dispatches every leaf to an <see cref="IFieldType"/>.
/// It never binds the payload to a CLR type — that is the whole question S1 exists to answer.
/// </summary>
public sealed class PayloadValidator(SchemaCatalog catalog)
{
    private const int MaxBlockDepth = 2; // v1 allows one level of nesting (spec §7.1)

    // ConfigurationJson is a string on the schema row. Parsing it per validation would dominate the
    // cost, so parsed configuration is cached by the schema object identity that owns it.
    private static readonly ConcurrentDictionary<string, JsonDocument> ConfigCache = new(StringComparer.Ordinal);

    private FieldTypeRegistry? _registry;

    public FieldTypeRegistry Registry =>
        _registry ?? throw new InvalidOperationException("Registry not bound.");

    public void Bind(FieldTypeRegistry registry) => _registry = registry;

    public ValidationContext Validate(JsonElement payload, ValidationMode mode)
    {
        var ctx = new ValidationContext(catalog, mode);
        Validate(payload, ctx);
        return ctx;
    }

    public void Validate(JsonElement payload, ValidationContext ctx)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            ctx.Error("payload.notAnObject", "The payload root must be a JSON object.");
            return;
        }

        if (!TryGetInt(payload, "schemaVersion", out var schemaVersion))
        {
            ctx.Error("payload.schemaVersion.missing", "The envelope must carry a numeric 'schemaVersion'.");
            return;
        }

        if (schemaVersion != 1)
        {
            ctx.Error("payload.schemaVersion.unsupported", $"schemaVersion {schemaVersion} is not supported by this build.");
            return;
        }

        if (!TryGetString(payload, "templateKey", out var templateKey) ||
            !TryGetInt(payload, "templateRevision", out var templateRevision))
        {
            ctx.Error("payload.template.missing", "The envelope must carry 'templateKey' and 'templateRevision'.");
            return;
        }

        if (!catalog.TryGetTemplate(templateKey, templateRevision, out var template))
        {
            // Spec §15.3: an unknown template key is a logged, degraded render — not an exception.
            ctx.Error("template.revision.unknown", $"No revision {templateRevision} of template '{templateKey}' is known.");
            return;
        }

        if (!payload.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Object)
        {
            ctx.Error("payload.zones.missing", "The envelope must carry a 'zones' object.");
            return;
        }

        using (ctx.Push("zones"))
        {
            foreach (var zoneSchema in template.Zones)
            {
                using var _ = ctx.Push(zoneSchema.Key);

                if (!zones.TryGetProperty(zoneSchema.Key, out var zoneValue))
                {
                    // Absent, not null: never authored. Only publish cares (spec §6.2, §8.5).
                    if (zoneSchema.IsRequired && ctx.Mode == ValidationMode.Publish)
                    {
                        ctx.Error("zone.required", $"Zone '{zoneSchema.Name}' is required and has no content.");
                    }

                    continue;
                }

                ValidateZone(zoneValue, zoneSchema, ctx, depth: 0);
            }

            foreach (var authored in zones.EnumerateObject())
            {
                if (!template.TryGetZone(authored.Name, out _))
                {
                    using var _ = ctx.Push(authored.Name);

                    // Spec §8.5: removing a zone must NOT destroy content. It becomes orphaned and
                    // is surfaced in the editor as an "Obsolete content" panel.
                    ctx.Warn("zone.orphaned", "No zone with this key exists in the template revision; the value is retained as orphaned content.");
                }
            }
        }
    }

    private void ValidateZone(JsonElement zoneValue, ZoneSchema zoneSchema, ValidationContext ctx, int depth)
    {
        if (zoneValue.ValueKind == JsonValueKind.Null)
        {
            if (zoneSchema.IsRequired && ctx.Mode == ValidationMode.Publish)
            {
                ctx.Error("zone.required", $"Zone '{zoneSchema.Name}' is required and was explicitly cleared.");
            }

            return;
        }

        if (zoneValue.ValueKind != JsonValueKind.Object)
        {
            ctx.Error("zone.notAnObject", $"Expected an object, found {zoneValue.ValueKind}.");
            return;
        }

        if (!TryGetString(zoneValue, "type", out var declaredType))
        {
            ctx.Error("zone.type.missing", "The zone value must declare its field type in 'type'.");
            return;
        }

        if (!string.Equals(declaredType, zoneSchema.FieldTypeKey, StringComparison.Ordinal))
        {
            ctx.Error(
                "zone.type.mismatch",
                $"Zone is defined as '{zoneSchema.FieldTypeKey}' but the payload declares '{declaredType}'. " +
                "A field-type change needs an explicit converter (spec §8.5).");
            return;
        }

        if (!Registry.TryGet(declaredType, out var fieldType))
        {
            // Spec §15.3: an unknown field type renders nothing and logs — it never throws.
            ctx.Warn("fieldType.unknown", $"No field type is registered for '{declaredType}'; the value will not render.");
            return;
        }

        var config = GetConfiguration(zoneSchema.ConfigurationJson);

        if (fieldType is BlocksFieldType)
        {
            ValidateBlockList(zoneValue, config, ctx, depth);
        }
        else
        {
            fieldType.Validate(zoneValue, config, ctx);
        }

        if (zoneSchema.IsRequired && ctx.Mode == ValidationMode.Publish && IsEmpty(zoneValue))
        {
            ctx.Error("zone.required", $"Zone '{zoneSchema.Name}' is required and has no content.");
        }
    }

    public void ValidateBlockList(in JsonElement value, in FieldConfiguration config, ValidationContext ctx) =>
        ValidateBlockList(value, config, ctx, depth: 0);

    private void ValidateBlockList(in JsonElement value, in FieldConfiguration config, ValidationContext ctx, int depth)
    {
        if (depth >= MaxBlockDepth)
        {
            ctx.Error("blocks.depth", $"Block nesting deeper than {MaxBlockDepth} levels is not supported in v1.");
            return;
        }

        if (!value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            ctx.Error("blocks.items.missing", "A blocks value must carry an 'items' array.");
            return;
        }

        var count = items.GetArrayLength();

        if (config.GetInt32("min") is { } min && count < min && ctx.Mode == ValidationMode.Publish)
        {
            ctx.Error("blocks.min", $"At least {min} block(s) are required; found {count}.");
        }

        if (config.GetInt32("max") is { } max && count > max)
        {
            ctx.Error("blocks.max", $"At most {max} block(s) are allowed; found {count}.");
        }

        var allowNesting = config.GetBoolean("allowNesting", fallback: false);
        HashSet<Guid>? seenIds = null;
        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            using var _ = ctx.Push($"[{index}]");
            index++;

            if (item.ValueKind != JsonValueKind.Object)
            {
                ctx.Error("block.notAnObject", $"Expected a block object, found {item.ValueKind}.");
                continue;
            }

            // The stable GUID is what makes the version diff report "moved" instead of
            // "removed + added" (spec §6.2, §11.4). A payload without it is not diffable.
            if (!TryGetString(item, "id", out var rawId) || !Guid.TryParse(rawId, out var id))
            {
                ctx.Error("block.id.missing", "Every block needs a stable GUID 'id'.");
            }
            else
            {
                seenIds ??= [];
                if (!seenIds.Add(id))
                {
                    ctx.Error("block.id.duplicate", $"Block id '{id}' appears more than once; ids must be unique within a list.");
                }
            }

            if (!TryGetString(item, "blockTypeKey", out var blockTypeKey) ||
                !TryGetInt(item, "blockTypeRevision", out var blockTypeRevision))
            {
                ctx.Error("block.type.missing", "Every block needs 'blockTypeKey' and 'blockTypeRevision'.");
                continue;
            }

            if (!config.ArrayContains("allowedBlockTypes", blockTypeKey))
            {
                ctx.Error("block.type.notAllowed", $"Block type '{blockTypeKey}' is not allowed in this zone.");
                continue;
            }

            if (!catalog.TryGetBlockType(blockTypeKey, blockTypeRevision, out var blockSchema))
            {
                ctx.Error("blockType.revision.unknown", $"No revision {blockTypeRevision} of block type '{blockTypeKey}' is known.");
                continue;
            }

            ValidateBlockProperties(item, blockSchema, ctx, depth, allowNesting);
        }
    }

    private void ValidateBlockProperties(
        in JsonElement block,
        BlockTypeRevisionSchema blockSchema,
        ValidationContext ctx,
        int depth,
        bool allowNesting)
    {
        var hasProperties = block.TryGetProperty("properties", out var properties) &&
                            properties.ValueKind == JsonValueKind.Object;

        if (!hasProperties)
        {
            ctx.Error("block.properties.missing", "A block must carry a 'properties' object.");
            return;
        }

        using var propertiesScope = ctx.Push("properties");

        foreach (var propertySchema in blockSchema.Properties)
        {
            using var propertyScope = ctx.Push(propertySchema.Key);

            if (!properties.TryGetProperty(propertySchema.Key, out var propertyValue))
            {
                if (propertySchema.IsRequired && ctx.Mode == ValidationMode.Publish)
                {
                    ctx.Error("property.required", $"'{propertySchema.Name}' is required and has no value.");
                }

                continue;
            }

            if (propertyValue.ValueKind == JsonValueKind.Null)
            {
                if (propertySchema.IsRequired && ctx.Mode == ValidationMode.Publish)
                {
                    ctx.Error("property.required", $"'{propertySchema.Name}' is required and was explicitly cleared.");
                }

                continue;
            }

            if (propertyValue.ValueKind != JsonValueKind.Object)
            {
                ctx.Error("property.notAnObject", $"Expected an object, found {propertyValue.ValueKind}.");
                continue;
            }

            if (!TryGetString(propertyValue, "type", out var declaredType))
            {
                ctx.Error("property.type.missing", "The property value must declare its field type in 'type'.");
                continue;
            }

            if (!string.Equals(declaredType, propertySchema.FieldTypeKey, StringComparison.Ordinal))
            {
                ctx.Error(
                    "property.type.mismatch",
                    $"Property is defined as '{propertySchema.FieldTypeKey}' but the payload declares '{declaredType}'.");
                continue;
            }

            if (!Registry.TryGet(declaredType, out var fieldType))
            {
                ctx.Warn("fieldType.unknown", $"No field type is registered for '{declaredType}'; the value will not render.");
                continue;
            }

            var config = GetConfiguration(propertySchema.ConfigurationJson);

            if (fieldType is BlocksFieldType)
            {
                if (!allowNesting)
                {
                    ctx.Error("blocks.nesting.notAllowed", "The containing zone is configured with allowNesting: false.");
                    continue;
                }

                ValidateBlockList(propertyValue, config, ctx, depth + 1);
            }
            else
            {
                fieldType.Validate(propertyValue, config, ctx);
            }

            if (propertySchema.IsRequired && ctx.Mode == ValidationMode.Publish && IsEmpty(propertyValue))
            {
                ctx.Error("property.required", $"'{propertySchema.Name}' is required and has no value.");
            }
        }

        foreach (var authored in properties.EnumerateObject())
        {
            if (!blockSchema.TryGetProperty(authored.Name, out _))
            {
                using var orphanScope = ctx.Push(authored.Name);
                ctx.Warn("property.orphaned", "No property with this key exists in the block type revision; the value is retained as orphaned content.");
            }
        }
    }

    // -----------------------------------------------------------------------------------------
    // Reference extraction — spec §7.3. Same walk, different visitor.
    // -----------------------------------------------------------------------------------------

    public List<ContentReference> ExtractReferences(JsonElement payload)
    {
        var references = new List<ContentReference>();
        var ctx = new ValidationContext(catalog, ValidationMode.Draft);

        if (payload.ValueKind != JsonValueKind.Object ||
            !TryGetString(payload, "templateKey", out var templateKey) ||
            !TryGetInt(payload, "templateRevision", out var templateRevision) ||
            !catalog.TryGetTemplate(templateKey, templateRevision, out var template) ||
            !payload.TryGetProperty("zones", out var zones) || zones.ValueKind != JsonValueKind.Object)
        {
            return references;
        }

        using var _ = ctx.Push("zones");

        foreach (var zoneSchema in template.Zones)
        {
            if (!zones.TryGetProperty(zoneSchema.Key, out var zoneValue) || zoneValue.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            using var __ = ctx.Push(zoneSchema.Key);

            if (!Registry.TryGet(zoneSchema.FieldTypeKey, out var fieldType))
            {
                continue;
            }

            fieldType.ExtractReferences(zoneValue, ctx, references);
        }

        return references;
    }

    public void ExtractBlockListReferences(in JsonElement value, ValidationContext ctx, List<ContentReference> into)
    {
        if (!value.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            using var _ = ctx.Push($"[{index}]");
            index++;

            if (item.ValueKind != JsonValueKind.Object ||
                !TryGetString(item, "blockTypeKey", out var blockTypeKey) ||
                !TryGetInt(item, "blockTypeRevision", out var revision) ||
                !catalog.TryGetBlockType(blockTypeKey, revision, out var blockSchema) ||
                !item.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            using var __ = ctx.Push("properties");

            foreach (var propertySchema in blockSchema.Properties)
            {
                if (!properties.TryGetProperty(propertySchema.Key, out var propertyValue) ||
                    propertyValue.ValueKind != JsonValueKind.Object ||
                    !Registry.TryGet(propertySchema.FieldTypeKey, out var fieldType))
                {
                    continue;
                }

                using var ___ = ctx.Push(propertySchema.Key);
                fieldType.ExtractReferences(propertyValue, ctx, into);
            }
        }
    }

    // -----------------------------------------------------------------------------------------

    private static FieldConfiguration GetConfiguration(string? configurationJson)
    {
        if (string.IsNullOrWhiteSpace(configurationJson))
        {
            return new FieldConfiguration(null);
        }

        var document = ConfigCache.GetOrAdd(configurationJson, static json => JsonDocument.Parse(json));
        return new FieldConfiguration(document.RootElement);
    }

    private static bool IsEmpty(in JsonElement value)
    {
        if (value.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            return items.GetArrayLength() == 0;
        }

        if (value.TryGetProperty("value", out var inner))
        {
            return inner.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => true,
                JsonValueKind.String => string.IsNullOrWhiteSpace(inner.GetString()),
                _ => false,
            };
        }

        if (value.TryGetProperty("mediaId", out var mediaId))
        {
            return mediaId.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        }

        if (value.TryGetProperty("reusableContentId", out var reusableId))
        {
            return reusableId.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
        }

        return false;
    }

    private static bool TryGetString(in JsonElement element, string name, out string value)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryGetInt(in JsonElement element, string name, out int value)
    {
        if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value))
        {
            return true;
        }

        value = 0;
        return false;
    }
}
