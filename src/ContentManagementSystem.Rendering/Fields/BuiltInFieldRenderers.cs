using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// The renderer each field type shipped with the CMS is drawn by (ADR-0014).
/// </summary>
/// <remarks>
/// This table exists because <c>IFieldType.RendererComponent</c> is <c>Type?</c> and every built-in
/// field type answers null: <c>Core</c> sits below <c>Rendering</c> in the reference graph and
/// cannot name a component in it. The mapping is therefore the hosting layer's to own, and this is
/// where it is owned.
/// <para>
/// Keyed by the field type key rather than by the field type's CLR type, because that key is what a
/// stored payload carries — the value has to be read by whatever wrote it, and the writer is
/// identified by a string.
/// </para>
/// <para>
/// A field type this table has no entry for is not an error here. It is reported by
/// <see cref="FieldRendererCatalog"/>, which is the only place that knows which field types this
/// deployment actually registered.
/// </para>
/// </remarks>
public static class BuiltInFieldRenderers
{
    /// <summary>The renderer component for each built-in field type key.</summary>
    public static IReadOnlyDictionary<string, Type> ByFieldTypeKey { get; } =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [FieldTypeKeys.PlainText] = typeof(PlainTextRenderer),
            [FieldTypeKeys.MultilineText] = typeof(MultilineTextRenderer),
            [FieldTypeKeys.RichText] = typeof(RichTextRenderer),
            [FieldTypeKeys.Html] = typeof(RawHtmlRenderer),
            [FieldTypeKeys.Number] = typeof(NumberRenderer),
            [FieldTypeKeys.Boolean] = typeof(BooleanRenderer),
            [FieldTypeKeys.Date] = typeof(DateRenderer),
            [FieldTypeKeys.DateTime] = typeof(DateTimeRenderer),
            [FieldTypeKeys.Choice] = typeof(ChoiceRenderer),
            [FieldTypeKeys.Color] = typeof(ColorRenderer),
            [FieldTypeKeys.Json] = typeof(JsonRenderer),
            [FieldTypeKeys.Media] = typeof(MediaRenderer),
            [FieldTypeKeys.MediaList] = typeof(MediaListRenderer),
            [FieldTypeKeys.Link] = typeof(LinkRenderer),
            [FieldTypeKeys.PageReference] = typeof(PageReferenceRenderer),
            [FieldTypeKeys.Reusable] = typeof(ReusableRenderer),
            [FieldTypeKeys.Blocks] = typeof(BlocksRenderer),
            [FieldTypeKeys.Tags] = typeof(TagsRenderer),
        };
}
