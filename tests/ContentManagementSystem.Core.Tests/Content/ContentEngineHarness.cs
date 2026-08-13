using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Tests.Content;

/// <summary>
/// Shared plumbing for the payload engine tests (tasks P1-14 to P1-17).
/// </summary>
/// <remarks>
/// Payloads and schemas are written as JSON and as explicit slot definitions rather than built by a
/// fluent helper, for the same reason the field type tests spell their values out: what is under
/// test is behaviour against the stored shape in spec section 6.2, and a test that states that shape
/// fails loudly when it changes.
/// </remarks>
internal static class ContentEngineHarness
{
    /// <summary>The field types the payload engine tests dispatch to.</summary>
    /// <remarks>
    /// A hand-picked set rather than the whole built-in catalogue: <c>richText</c> and <c>html</c>
    /// need a sanitizer that does not exist until P1-18, and nothing here is about them.
    /// </remarks>
    public static IFieldTypeRegistry Registry { get; } = BuildRegistry();

    /// <summary>Builds a validator over <see cref="Registry"/> and a catalog.</summary>
    /// <param name="catalog">The schema catalog, or null for one that knows nothing.</param>
    public static ContentSchemaValidator Validator(IContentSchemaCatalog? catalog = null) =>
        new(Registry, catalog ?? ContentSchemaCatalog.Empty);

    /// <summary>Builds a reference indexer over <see cref="Registry"/>.</summary>
    public static ReferenceIndexer Indexer() => new(Registry);

    /// <summary>Builds a catalog from one template revision and any block type revisions.</summary>
    /// <param name="template">The template revision.</param>
    /// <param name="blockTypes">The block type revisions.</param>
    public static ContentSchemaCatalog Catalog(ContentSchema template, params BlockTypeSchema[] blockTypes) =>
        new([template], blockTypes);

    /// <summary>Builds a template revision's schema.</summary>
    /// <param name="templateKey">Key of the template.</param>
    /// <param name="revision">The revision number.</param>
    /// <param name="zones">The zone definitions.</param>
    public static ContentSchema Template(
        string templateKey,
        int revision,
        params ContentPropertySchema[] zones) =>
        new(templateKey, revision, zones);

    /// <summary>Builds a block type revision's schema.</summary>
    /// <param name="blockTypeKey">Key of the block type.</param>
    /// <param name="revision">The revision number.</param>
    /// <param name="properties">The property definitions.</param>
    public static BlockTypeSchema BlockType(
        string blockTypeKey,
        int revision,
        params ContentPropertySchema[] properties) =>
        new(blockTypeKey, revision, properties);

    /// <summary>Builds one zone or block-type property definition.</summary>
    /// <param name="key">The stable key.</param>
    /// <param name="fieldTypeKey">Key of the field type that fills it.</param>
    /// <param name="configurationJson">Field-type configuration, if any.</param>
    /// <param name="isRequired">Whether an empty value blocks publishing.</param>
    public static ContentPropertySchema Slot(
        string key,
        string fieldTypeKey,
        string? configurationJson = null,
        bool isRequired = false) =>
        ContentPropertySchema.Create(key, key, fieldTypeKey, configurationJson, isRequired);

    /// <summary>Parses payload JSON.</summary>
    /// <param name="json">The payload text.</param>
    public static ContentPayload Payload(string json) => ContentPayload.Parse(json);

    /// <summary>Validates a payload written as JSON against a catalog.</summary>
    /// <param name="catalog">The schema catalog.</param>
    /// <param name="payloadJson">The payload text.</param>
    /// <param name="mode">Draft or publish.</param>
    public static ValueTask<ContentValidationReport> ValidateAsync(
        this IContentSchemaCatalog catalog,
        string payloadJson,
        ValidationMode mode = ValidationMode.Draft) =>
        Validator(catalog).ValidateAsync(Payload(payloadJson), mode, TestContext.Current.CancellationToken);

    /// <summary>Validates a payload written as JSON against a schema chosen by the caller.</summary>
    /// <param name="schema">The template revision to check against.</param>
    /// <param name="payloadJson">The payload text.</param>
    /// <param name="mode">Draft or publish.</param>
    /// <param name="catalog">The catalog block types are resolved through.</param>
    public static ValueTask<ContentValidationReport> ValidateAsync(
        this ContentSchema schema,
        string payloadJson,
        ValidationMode mode = ValidationMode.Draft,
        IContentSchemaCatalog? catalog = null) =>
        Validator(catalog).ValidateAsync(
            Payload(payloadJson),
            schema,
            mode,
            TestContext.Current.CancellationToken);

    /// <summary>The codes reported, for asserting on without pinning message wording.</summary>
    /// <param name="report">The report.</param>
    public static IEnumerable<string> Codes(this ContentValidationReport report) =>
        report.Diagnostics.Select(diagnostic => diagnostic.Code);

    /// <summary>The absolute payload paths reported.</summary>
    /// <param name="report">The report.</param>
    public static IEnumerable<string> Paths(this ContentValidationReport report) =>
        report.Diagnostics.Select(diagnostic => diagnostic.Path);

    private static IFieldTypeRegistry BuildRegistry()
    {
        var registry = null as IFieldTypeRegistry;
        var deferred = new Lazy<IFieldTypeRegistry>(() => registry!);

        registry = new FieldTypeRegistry(
            [
                new PlainTextFieldType(),
                new MultilineTextFieldType(),
                new NumberFieldType(),
                new BooleanFieldType(),
                new ChoiceFieldType(),
                new MediaFieldType(),
                new MediaListFieldType(),
                new LinkFieldType(),
                new PageReferenceFieldType(),
                new ReusableFieldType(),
                new TagsFieldType(),
                new BlocksFieldType(deferred),
            ]);

        return registry;
    }
}
