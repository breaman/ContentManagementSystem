using System.Text.Json;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Walks a payload against a runtime-defined schema, dispatching every value to the field type that
/// owns it (spec section 6.2).
/// </summary>
/// <remarks>
/// The division of labour is the point of this class. A field type knows what its own value should
/// look like and nothing else; this walk knows the envelope, which revision was captured, which keys
/// the schema accounts for, and where in the document each value sits. Neither half can produce a
/// useful diagnostic alone — which is why the field type reports a path relative to its value and
/// this prefixes the rest.
/// <para>
/// Four constraints come from the S1 spike and are load-bearing rather than stylistic:
/// the payload is never bound to a CLR model, or absent-versus-null is lost; the diagnostic path is
/// a push/pop stack, so the happy path allocates nothing; every diagnostic carries a stable code
/// beside its message; and draft-versus-publish is a parameter of the walk rather than a filter
/// applied to its results.
/// </para>
/// <para>
/// Structural problems are errors and lost schema is a warning, consistently: an orphaned zone, an
/// unknown block type, a field type no longer registered — all of these describe content that
/// outlived the structure it was written against, and refusing the save would strand the page
/// instead of letting an editor fix it (spec sections 8.5 and 15.3).
/// </para>
/// </remarks>
public sealed class ContentSchemaValidator : IContentSchemaValidator
{
    private readonly IFieldTypeRegistry _registry;
    private readonly IContentSchemaCatalog _catalog;

    /// <summary>
    /// Creates the validator.
    /// </summary>
    /// <param name="registry">The field types this deployment knows about.</param>
    /// <param name="catalog">Resolves the captured revisions a payload names.</param>
    public ContentSchemaValidator(IFieldTypeRegistry registry, IContentSchemaCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(catalog);

        _registry = registry;
        _catalog = catalog;
    }

    /// <inheritdoc />
    public async ValueTask<ContentValidationReport> ValidateAsync(
        ContentPayload payload,
        ValidationMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var walk = new Walk(_registry, _catalog, mode);

        if (!walk.ValidateEnvelope(payload)) return walk.Report();

        if (payload.TemplateKey is not { Length: > 0 } templateKey ||
            payload.TemplateRevision is not { } templateRevision)
        {
            walk.AddError(
                ContentValidationCodes.TemplateMissing,
                "The payload does not say which template revision it was authored against, so there " +
                "is no schema to check it against.");

            return walk.Report();
        }

        if (!_catalog.TryGetTemplate(templateKey, templateRevision, out var schema))
        {
            walk.AddError(
                ContentValidationCodes.TemplateUnknown,
                $"No revision {templateRevision} of template '{templateKey}' is known to this deployment.");

            return walk.Report();
        }

        await walk.ValidateZonesAsync(payload, schema, cancellationToken);

        return walk.Report();
    }

    /// <inheritdoc />
    public async ValueTask<ContentValidationReport> ValidateAsync(
        ContentPayload payload,
        ContentSchema schema,
        ValidationMode mode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(schema);

        var walk = new Walk(_registry, _catalog, mode);

        if (!walk.ValidateEnvelope(payload)) return walk.Report();

        await walk.ValidateZonesAsync(payload, schema, cancellationToken);

        return walk.Report();
    }

    /// <summary>
    /// One traversal of one payload.
    /// </summary>
    /// <remarks>
    /// Everything mutable lives here rather than on the validator, which is a singleton walked
    /// concurrently by every request that saves content.
    /// </remarks>
    private sealed class Walk(IFieldTypeRegistry registry, IContentSchemaCatalog catalog, ValidationMode mode)
    {
        /// <summary>
        /// How deep the walk will follow nested blocks.
        /// </summary>
        /// <remarks>
        /// Not the nesting rule — the <c>blocks</c> field type enforces that, at one level for v1,
        /// and reports it against the property that broke it. This is the guard that keeps a
        /// hand-edited or migrated payload from turning a save into a stack overflow, which is why
        /// it sits well above anything the editor can produce.
        /// </remarks>
        private const int MaxBlockDepth = 8;

        private readonly List<ContentValidationDiagnostic> _diagnostics = [];
        private readonly ContentPath _path = new();

        private string? _zoneKey;
        private Guid? _blockId;
        private string? _propertyKey;

        /// <summary>Turns what the walk found into a report.</summary>
        /// <returns>The report.</returns>
        public ContentValidationReport Report() =>
            _diagnostics.Count == 0 ? ContentValidationReport.Empty : new ContentValidationReport(_diagnostics);

        /// <summary>Adds a blocking diagnostic at the current position.</summary>
        /// <param name="code">Stable machine-readable discriminator.</param>
        /// <param name="message">Human-readable explanation.</param>
        public void AddError(string code, string message) =>
            Add(code, message, ValidationSeverity.Error, relativePath: null);

        /// <summary>
        /// Checks the parts of the envelope that must hold before anything below can be read.
        /// </summary>
        /// <param name="payload">The payload.</param>
        /// <returns><see langword="false"/> when the walk cannot usefully continue.</returns>
        public bool ValidateEnvelope(ContentPayload payload)
        {
            if (!payload.IsObject)
            {
                AddError(ContentValidationCodes.PayloadShape, "A content payload is a JSON object.");

                return false;
            }

            if (payload.SchemaVersion is not { } schemaVersion)
            {
                AddError(
                    ContentValidationCodes.SchemaVersionMissing,
                    $"The payload envelope must carry a numeric '{ContentPayloadMembers.SchemaVersion}'.");

                return false;
            }

            if (schemaVersion != ContentPayload.CurrentSchemaVersion)
            {
                // Including a version from the future: a node mid-rollback must refuse content it
                // cannot read rather than validate it against assumptions that no longer hold.
                AddError(
                    ContentValidationCodes.SchemaVersionUnsupported,
                    $"Payload schema version {schemaVersion} cannot be read by this build, which reads " +
                    $"version {ContentPayload.CurrentSchemaVersion}.");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks every zone the schema declares, then everything the payload holds that it does not.
        /// </summary>
        /// <param name="payload">The payload.</param>
        /// <param name="schema">The template revision to check against.</param>
        /// <param name="cancellationToken">Token observed while validating.</param>
        public async ValueTask ValidateZonesAsync(
            ContentPayload payload,
            ContentSchema schema,
            CancellationToken cancellationToken)
        {
            if (!payload.HasZones)
            {
                AddError(
                    ContentValidationCodes.ZonesMissing,
                    $"The payload envelope must carry a '{ContentPayloadMembers.Zones}' object.");

                return;
            }

            using var zones = _path.Push(ContentPayloadMembers.Zones);

            foreach (var zone in schema.Zones)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _zoneKey = zone.Key;
                _propertyKey = zone.Key;

                using var scope = _path.Push(zone.Key);

                // Absent zones are dispatched too, as an undefined element. The field types are
                // written to expect that — "never authored" is a state they have to rule on, and it
                // is the only way a required zone that was never filled in gets reported at all.
                await ValidateSlotAsync(payload.GetZone(zone.Key), zone, depth: 0, cancellationToken);
            }

            _zoneKey = null;
            _propertyKey = null;

            foreach (var authored in payload.ZoneKeys)
            {
                if (schema.DeclaresZone(authored)) continue;

                _zoneKey = authored;
                _propertyKey = authored;

                using var scope = _path.Push(authored);

                Add(
                    ContentValidationCodes.ZoneOrphaned,
                    "The template no longer defines this zone. Its content is kept and shown as " +
                    "obsolete content until an editor discards it.",
                    ValidationSeverity.Warning,
                    relativePath: null);
            }

            _zoneKey = null;
            _propertyKey = null;
        }

        private void Add(
            string code,
            string message,
            ValidationSeverity severity,
            string? relativePath) =>
            _diagnostics.Add(new ContentValidationDiagnostic(code, message, severity, _path.Combine(relativePath))
            {
                ZoneKey = _zoneKey,
                BlockId = _blockId,
                PropertyKey = _propertyKey,
            });

        /// <summary>
        /// Dispatches one value to its field type, then walks into it when it holds blocks.
        /// </summary>
        /// <param name="value">The stored value, or an undefined element when it was never authored.</param>
        /// <param name="slot">The zone or block-type property the value fills.</param>
        /// <param name="depth">How many block levels deep the walk already is.</param>
        /// <param name="cancellationToken">Token observed while validating.</param>
        private async ValueTask ValidateSlotAsync(
            JsonElement value,
            ContentPropertySchema slot,
            int depth,
            CancellationToken cancellationToken)
        {
            if (registry.Find(slot.FieldTypeKey) is not { } fieldType)
            {
                // The deployment has lost a field type the structure still names. Nothing can check
                // the value and nothing will render it, which spec section 15.3 makes a warning.
                Add(
                    ContentValidationCodes.FieldTypeUnknown,
                    $"No field type is registered for '{slot.FieldTypeKey}', so '{slot.Name}' cannot " +
                    "be checked and will not render.",
                    ValidationSeverity.Warning,
                    relativePath: null);

                return;
            }

            var result = await fieldType.ValidateAsync(value, slot.Configuration, mode, cancellationToken);

            foreach (var diagnostic in result.Diagnostics)
            {
                Add(diagnostic.Code, diagnostic.Message, diagnostic.Severity, diagnostic.RelativePath);
            }

            // A container holds values belonging to other field types, and only the schema knows what
            // those are meant to be — the field type validated the list, not what is inside it. The
            // shape check is deliberate: this walks the block shape spec section 6.2 defines, and a
            // container storing something else is left entirely to its own ValidateAsync.
            if (fieldType.Capabilities.HasFlag(FieldTypeCapabilities.Container) &&
                value.ValueKind is JsonValueKind.Object &&
                value.TryGetProperty(BlocksFieldType.ItemsMember, out var items) &&
                items.ValueKind is JsonValueKind.Array)
            {
                await ValidateBlocksAsync(items, depth, cancellationToken);
            }
        }

        /// <summary>
        /// Checks each block's properties against the block type revision it captured.
        /// </summary>
        /// <param name="items">The block instances.</param>
        /// <param name="depth">How many block levels deep the walk already is.</param>
        /// <param name="cancellationToken">Token observed while validating.</param>
        private async ValueTask ValidateBlocksAsync(
            JsonElement items,
            int depth,
            CancellationToken cancellationToken)
        {
            using var itemsScope = _path.Push(BlocksFieldType.ItemsMember);

            if (depth >= MaxBlockDepth)
            {
                AddError(
                    ContentValidationCodes.BlockDepth,
                    $"Blocks are nested more than {MaxBlockDepth} levels deep; the content below this " +
                    "point cannot be checked.");

                return;
            }

            var index = 0;
            var enclosingBlockId = _blockId;
            var enclosingPropertyKey = _propertyKey;

            foreach (var item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var itemScope = _path.PushIndex(index);

                index++;

                // Everything skipped here — a block that is not an object, one with no block type
                // key, one with an unusable id — has already been reported by the blocks field type
                // against this same path. Reporting it a second time in the walk's own words would
                // put two diagnostics on one defect.
                if (item.ValueKind is not JsonValueKind.Object) continue;

                _blockId = ReadBlockId(item);

                if (ReadBlockTypeKey(item) is not { } blockTypeKey) continue;

                if (!catalog.TryGetBlockType(blockTypeKey, ReadBlockTypeRevision(item), out var blockType))
                {
                    _propertyKey = null;

                    Add(
                        ContentValidationCodes.BlockTypeUnknown,
                        $"Block type '{blockTypeKey}' is not known to this deployment at the revision " +
                        "this block captured, so its properties cannot be checked.",
                        ValidationSeverity.Warning,
                        relativePath: null);

                    continue;
                }

                await ValidateBlockPropertiesAsync(item, blockType, depth, cancellationToken);
            }

            _blockId = enclosingBlockId;
            _propertyKey = enclosingPropertyKey;
        }

        private async ValueTask ValidateBlockPropertiesAsync(
            JsonElement block,
            BlockTypeSchema blockType,
            int depth,
            CancellationToken cancellationToken)
        {
            block.TryGetProperty(BlocksFieldType.PropertiesMember, out var properties);

            var hasProperties = properties.ValueKind is JsonValueKind.Object;

            // A properties member of some other shape entirely is the blocks field type's to report;
            // walking into it would put a second diagnostic on the same defect. Absent or cleared is
            // not that: a block that authored nothing is still checked against every property its
            // type declares, which is the only way a required one that was never filled in surfaces.
            if (!hasProperties && properties.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
            {
                return;
            }

            await ValidateDeclaredPropertiesAsync(properties, blockType, depth, cancellationToken);

            if (!hasProperties) return;

            using var scope = _path.Push(BlocksFieldType.PropertiesMember);

            foreach (var authored in properties.EnumerateObject())
            {
                if (blockType.DeclaresProperty(authored.Name)) continue;

                _propertyKey = authored.Name;

                using var propertyScope = _path.Push(authored.Name);

                Add(
                    ContentValidationCodes.PropertyOrphaned,
                    $"Block type '{blockType.BlockTypeKey}' no longer defines this property. Its value " +
                    "is kept and shown as obsolete content until an editor discards it.",
                    ValidationSeverity.Warning,
                    relativePath: null);
            }

            _propertyKey = null;
        }

        private async ValueTask ValidateDeclaredPropertiesAsync(
            JsonElement properties,
            BlockTypeSchema blockType,
            int depth,
            CancellationToken cancellationToken)
        {
            using var scope = _path.Push(BlocksFieldType.PropertiesMember);

            foreach (var property in blockType.Properties)
            {
                cancellationToken.ThrowIfCancellationRequested();

                _propertyKey = property.Key;

                using var propertyScope = _path.Push(property.Key);

                var value = properties.ValueKind is JsonValueKind.Object &&
                            properties.TryGetProperty(property.Key, out var stored)
                    ? stored
                    : default;

                await ValidateSlotAsync(value, property, depth + 1, cancellationToken);
            }

            _propertyKey = null;
        }

        private static Guid? ReadBlockId(JsonElement block) =>
            block.TryGetProperty(BlocksFieldType.IdMember, out var id) &&
            id.ValueKind is JsonValueKind.String &&
            Guid.TryParse(id.GetString(), out var parsed)
                ? parsed
                : null;

        private static string? ReadBlockTypeKey(JsonElement block) =>
            block.TryGetProperty(BlocksFieldType.BlockTypeKeyMember, out var key) &&
            key.ValueKind is JsonValueKind.String &&
            key.GetString() is { Length: > 0 } blockTypeKey
                ? blockTypeKey
                : null;

        private static int? ReadBlockTypeRevision(JsonElement block) =>
            block.TryGetProperty(BlocksFieldType.BlockTypeRevisionMember, out var revision) &&
            revision.ValueKind is JsonValueKind.Number &&
            revision.TryGetInt32(out var parsed)
                ? parsed
                : null;
    }
}
