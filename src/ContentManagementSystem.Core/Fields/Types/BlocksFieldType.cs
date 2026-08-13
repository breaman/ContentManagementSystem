using System.Text;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// An ordered list of block instances — the field type that turns a zone into a page builder
/// (spec sections 6.2 and 7.1).
/// </summary>
/// <remarks>
/// Stored as
/// <c>{ "type": "blocks", "items": [ { "id": "0f6c…", "blockTypeKey": "hero-banner",
/// "blockTypeRevision": 3, "properties": { … } } ] }</c>.
/// <para>
/// Each block's <c>id</c> is a GUID generated when the block is added and preserved for the life of
/// that block. It is what lets the version diff report a reordered block as <em>moved</em> rather
/// than as removed-plus-added (spec section 11.4), so it is required rather than optional, and a
/// repeated one is an error: two blocks sharing an id make the diff ambiguous and the block editor
/// address the wrong one.
/// </para>
/// <para>
/// Configuration keys: <c>allowedBlockTypes</c>, <c>min</c> / <c>max</c>, <c>allowNesting</c>
/// (one level in v1).
/// </para>
/// <para>
/// <strong>What this field type does not do:</strong> check a block's properties against its block
/// type. That needs the <c>BlockTypeProperty</c> definitions and each property's own configuration,
/// which is the schema walk's to load and dispatch (P1-15). The division is not arbitrary —
/// <c>ValidateAsync</c> and <c>SanitizeAsync</c> both take a <see cref="FieldConfiguration"/>
/// belonging to one schema row, and this field type holds the row for the <em>list</em>, not for
/// anything inside it. <c>ExtractReferences</c> and <c>ExtractSearchText</c> take no configuration
/// and need none, so those it completes itself, all the way down.
/// </para>
/// </remarks>
public sealed class BlocksFieldType : ListFieldTypeBase
{
    /// <summary>The member holding the block instances.</summary>
    public const string ItemsMember = "items";

    /// <summary>The stable GUID identifying one block across edits and versions.</summary>
    public const string IdMember = "id";

    /// <summary>The block type the instance was authored against.</summary>
    public const string BlockTypeKeyMember = "blockTypeKey";

    /// <summary>The block type revision whose schema was captured at write time.</summary>
    public const string BlockTypeRevisionMember = "blockTypeRevision";

    /// <summary>The block's own property values, keyed by property alias.</summary>
    public const string PropertiesMember = "properties";

    /// <summary>
    /// How deep reference and search extraction will walk, whatever the payload says.
    /// </summary>
    /// <remarks>
    /// Validation refuses more than one level of nesting, so anything deeper is a hand-edited or
    /// migrated payload rather than something the editor can produce. The guard exists so that such
    /// a payload is quietly truncated instead of overflowing the stack on a public request — the
    /// delivery path never fails a request over content it cannot interpret (spec section 15.3).
    /// </remarks>
    private const int MaxWalkDepth = 8;

    private readonly Lazy<IFieldTypeRegistry> _registry;

    /// <summary>
    /// Creates the field type.
    /// </summary>
    /// <param name="registry">The registry, used to dispatch to the field types of nested values.</param>
    /// <remarks>
    /// Deferred because the dependency is circular by nature: the registry is built from every
    /// registered field type, this among them. Resolving it on first use rather than in the
    /// constructor breaks the cycle without a service locator, and by then the registry is a
    /// constructed singleton.
    /// </remarks>
    public BlocksFieldType(Lazy<IFieldTypeRegistry> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        _registry = registry;
    }

    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Blocks;

    /// <inheritdoc />
    public override string DisplayName => "Blocks";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities =>
        FieldTypeCapabilities.Container |
        FieldTypeCapabilities.ReferenceBearing |
        FieldTypeCapabilities.Searchable;
    /// <inheritdoc />
    public override FieldConfigurationSchema ConfigurationSchema { get; } = ListConfigurationSchema.Extend(
        [
            FieldConfigurationSetting.TextList(
                "allowedBlockTypes",
                "Keys of the block types an editor may add. An empty list allows any registered block type."),
            FieldConfigurationSetting.Boolean(
                "allowNesting",
                "Whether a block may contain further blocks. One level only in v1."),
        ]);


    /// <inheritdoc />
    protected override string PayloadMember => ItemsMember;

    /// <inheritdoc />
    protected override string ItemNoun => "blocks";

    /// <inheritdoc />
    protected override void ValidateItems(
        JsonElement items,
        FieldConfiguration configuration,
        ValidationMode mode,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var allowed = configuration.GetStringArray("allowedBlockTypes");
        var allowedNestingDepth = configuration.GetBoolean("allowNesting") ? 1 : 0;
        var ids = new HashSet<Guid>();
        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            var path = ItemPath(index);

            index++;

            if (item.ValueKind is not JsonValueKind.Object)
            {
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.Shape,
                    "Expected a block.",
                    path);

                continue;
            }

            ValidateId(item, path, ids, ref diagnostics);
            ValidateBlockType(item, path, allowed, ref diagnostics);
            ValidateRevision(item, path, ref diagnostics);
            ValidateProperties(item, path, allowedNestingDepth, depth: 0, ref diagnostics);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Walks every block's every property and dispatches each to its own field type, so that an
    /// image inside a card inside a zone is reported exactly as one sitting directly in the zone.
    /// A container that reported only itself would drop every nested reference, and the symptom —
    /// a page that stops invalidating when the image it shows is replaced — is close to
    /// untraceable (spec section 7.3).
    /// <para>
    /// Dispatch is by the <c>type</c> written into each stored value, not by the schema. A value
    /// has to be read by whatever field type wrote it: if the block type's property has since been
    /// changed to a different field type, the schema names something that cannot interpret what is
    /// stored, and asking it would silently lose the reference the old value still holds. A nested
    /// property with no discriminator at all is skipped for the same reason — there is nothing to
    /// dispatch on.
    /// </para>
    /// </remarks>
    public override IEnumerable<ContentReference> ExtractReferences(JsonElement value)
    {
        var references = new List<ContentReference>();

        Walk(
            value,
            prefix: null,
            depth: 0,
            (fieldType, property, path) =>
            {
                foreach (var reference in fieldType.ExtractReferences(property))
                {
                    references.Add(reference with
                    {
                        Path = RelativePaths.Combine(path, reference.Path),
                    });
                }
            });

        return references;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Delegates to the nested field types in document order, so a page's headline is indexed ahead
    /// of the body it introduces. Without this every word inside a block zone would be invisible to
    /// search, which on a block-built site is most of the site.
    /// </remarks>
    public override string ExtractSearchText(JsonElement value)
    {
        var builder = new StringBuilder();

        Walk(
            value,
            prefix: null,
            depth: 0,
            (fieldType, property, _) =>
            {
                var text = fieldType.ExtractSearchText(property);

                if (string.IsNullOrEmpty(text)) return;

                if (builder.Length > 0) builder.Append(' ');

                builder.Append(text);
            });

        return SearchText.Collapse(builder.ToString());
    }

    private static void ValidateId(
        JsonElement item,
        string path,
        HashSet<Guid> ids,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var idPath = RelativePaths.Member(path, IdMember);

        if (!item.TryGetProperty(IdMember, out var id) ||
            id.ValueKind is not JsonValueKind.String ||
            !Guid.TryParse(id.GetString(), out var parsed))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.BlockId,
                "Every block needs a stable id, so that a version diff can follow it when it moves.",
                idPath);

            return;
        }

        if (!ids.Add(parsed))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.Duplicate,
                "Two blocks share this id; a diff cannot tell them apart.",
                idPath);
        }
    }

    private static void ValidateBlockType(
        JsonElement item,
        string path,
        string[] allowed,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var keyPath = RelativePaths.Member(path, BlockTypeKeyMember);

        if (!item.TryGetProperty(BlockTypeKeyMember, out var key) ||
            key.ValueKind is not JsonValueKind.String ||
            key.GetString() is not { Length: > 0 } blockTypeKey ||
            string.IsNullOrWhiteSpace(blockTypeKey))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.BlockTypeKey,
                "Every block names the block type it was authored against.",
                keyPath);

            return;
        }

        // An empty allowlist means the property accepts any block type. That a block type by this
        // key is registered at all is not checked here: an unknown key renders nothing and logs,
        // exactly as an unknown field type key does, and refusing to save content authored against
        // a block type that was later removed would strand the page rather than let an editor fix
        // it (spec section 15.3).
        if (allowed.Length > 0 && Array.IndexOf(allowed, blockTypeKey) < 0)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.BlockNotAllowed,
                $"'{blockTypeKey}' is not one of the block types this property accepts.",
                keyPath);
        }
    }

    private static void ValidateRevision(
        JsonElement item,
        string path,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        if (!item.TryGetProperty(BlockTypeRevisionMember, out var revision) ||
            revision.ValueKind is JsonValueKind.Null)
        {
            // Absent is tolerated: the revision is the schema captured at write time (spec section
            // 8.5), and a payload written before revisions were captured still renders against the
            // current one.
            return;
        }

        if (!StoredId.TryRead(revision, out _))
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.BlockRevision,
                "A captured block type revision is a positive revision number.",
                RelativePaths.Member(path, BlockTypeRevisionMember));
        }
    }

    private void ValidateProperties(
        JsonElement item,
        string path,
        int allowedNestingDepth,
        int depth,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var properties = GetMember(item, PropertiesMember);

        if (properties.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return;

        var propertiesPath = RelativePaths.Member(path, PropertiesMember);

        if (properties.ValueKind is not JsonValueKind.Object)
        {
            Diagnostics.AddError(
                ref diagnostics,
                FieldValidationCodes.Shape,
                "A block's properties are an object keyed by property alias.",
                propertiesPath);

            return;
        }

        // Only nesting is checked here. The values themselves belong to the block type's own
        // properties, each with its own configuration, and validating them is the schema walk's
        // (P1-15) — see the note on this class.
        foreach (var property in properties.EnumerateObject())
        {
            if (ReadTypeKey(property.Value) != Key) continue;

            var nestedPath = RelativePaths.Member(propertiesPath, property.Name);

            if (depth + 1 > allowedNestingDepth)
            {
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.BlockNesting,
                    allowedNestingDepth == 0
                        ? "This property does not allow blocks inside blocks."
                        : "Blocks may be nested one level deep.",
                    nestedPath);

                continue;
            }

            var nestedItems = GetMember(property.Value, ItemsMember);

            if (nestedItems.ValueKind is not JsonValueKind.Array) continue;

            var index = 0;

            foreach (var nested in nestedItems.EnumerateArray())
            {
                ValidateProperties(
                    nested,
                    RelativePaths.Index(RelativePaths.Member(nestedPath, ItemsMember), index),
                    allowedNestingDepth,
                    depth + 1,
                    ref diagnostics);

                index++;
            }
        }
    }

    /// <summary>
    /// Visits every nested property value with the field type that wrote it.
    /// </summary>
    /// <param name="blocksProperty">A stored <c>blocks</c> property.</param>
    /// <param name="prefix">Path of that property relative to this one, or null for this one.</param>
    /// <param name="depth">How many block levels deep the walk already is.</param>
    /// <param name="visit">Called with the field type, the stored value, and its relative path.</param>
    private void Walk(
        JsonElement blocksProperty,
        string? prefix,
        int depth,
        Action<IFieldType, JsonElement, string> visit)
    {
        if (depth > MaxWalkDepth) return;

        var items = GetMember(blocksProperty, ItemsMember);

        if (items.ValueKind is not JsonValueKind.Array) return;

        var itemsPath = RelativePaths.Member(prefix, ItemsMember);
        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            var propertiesPath = RelativePaths.Member(
                RelativePaths.Index(itemsPath, index),
                PropertiesMember);

            index++;

            var properties = GetMember(item, PropertiesMember);

            if (properties.ValueKind is not JsonValueKind.Object) continue;

            foreach (var property in properties.EnumerateObject())
            {
                if (ReadTypeKey(property.Value) is not { } typeKey) continue;

                var path = RelativePaths.Member(propertiesPath, property.Name);

                if (typeKey == Key)
                {
                    Walk(property.Value, path, depth + 1, visit);

                    continue;
                }

                // A key nothing is registered under is skipped rather than thrown on: content
                // outlives the code deployed when it was written (spec section 15.3).
                if (_registry.Value.Find(typeKey) is { } fieldType)
                {
                    visit(fieldType, property.Value, path);
                }
            }
        }
    }

    private static string? ReadTypeKey(JsonElement property) =>
        property.ValueKind is JsonValueKind.Object &&
        property.TryGetProperty(TypeMember, out var type) &&
        type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;
}
