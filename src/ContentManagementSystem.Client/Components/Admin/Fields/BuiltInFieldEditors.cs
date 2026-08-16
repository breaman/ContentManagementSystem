using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Client.Components.Admin.Fields.BlockList;
using ContentManagementSystem.Client.Components.Admin.Fields.Html;
using ContentManagementSystem.Client.Components.Admin.Fields.Reference;
using ContentManagementSystem.Client.Components.Admin.Fields.RichText;
using ContentManagementSystem.Client.Components.Admin.Fields.Scalar;
using ContentManagementSystem.Client.Components.Admin.Fields.Text;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <summary>
/// The editor each field type shipped with the CMS is filled in with (ADR-0014, tasks P6-06 to P6-15).
/// </summary>
/// <remarks>
/// The mirror image of <c>BuiltInFieldRenderers</c> in <c>Rendering</c>, and it exists for the same
/// reason: <c>IFieldType.EditorComponent</c> is <c>Type?</c> and every built-in field type answers
/// null, because <c>Core</c> cannot name a component in <c>Client</c> without a reference cycle. The
/// mapping is the hosting layer's to own, and this is where the backoffice owns it.
/// <para>
/// Keyed by the field type key rather than by the field type's CLR type, because that key is what a
/// captured slot and a stored value both carry, and because the backoffice never sees the field type
/// object at all — it runs in WebAssembly, where <c>Core</c> is not present.
/// </para>
/// <para>
/// Every entry here takes the same three parameters — <c>Field</c>, <c>Value</c>,
/// <c>ValueChanged</c> — enforced by <see cref="FieldEditorBase"/>, which is what lets
/// <see cref="FieldEditorHost"/> render any of them without a switch statement.
/// </para>
/// </remarks>
public static class BuiltInFieldEditors
{
    /// <summary>The editor component for each built-in field type key.</summary>
    public static IReadOnlyDictionary<string, Type> ByFieldTypeKey { get; } =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [FieldTypeKeys.PlainText] = typeof(PlainTextEditor),
            [FieldTypeKeys.MultilineText] = typeof(MultilineTextEditor),
            [FieldTypeKeys.RichText] = typeof(RichTextFieldEditor),
            [FieldTypeKeys.Html] = typeof(HtmlFieldEditor),
            [FieldTypeKeys.Number] = typeof(NumberEditor),
            [FieldTypeKeys.Boolean] = typeof(BooleanEditor),
            [FieldTypeKeys.Date] = typeof(DateEditor),
            [FieldTypeKeys.DateTime] = typeof(DateTimeEditor),
            [FieldTypeKeys.Choice] = typeof(ChoiceEditor),
            [FieldTypeKeys.Color] = typeof(ColorEditor),
            [FieldTypeKeys.Json] = typeof(JsonEditor),
            [FieldTypeKeys.Tags] = typeof(TagsEditor),
            [FieldTypeKeys.Media] = typeof(MediaFieldEditor),
            [FieldTypeKeys.MediaList] = typeof(MediaListEditor),
            [FieldTypeKeys.Link] = typeof(LinkEditor),
            [FieldTypeKeys.PageReference] = typeof(PageReferenceEditor),
            [FieldTypeKeys.Reusable] = typeof(ReusablePlacementEditor),
            [FieldTypeKeys.Blocks] = typeof(BlockListEditor),
        };

    /// <summary>
    /// What draws a field type with no entry above.
    /// </summary>
    /// <remarks>
    /// A deployment can register a field type this build has never heard of, and the honest thing to
    /// show for it is what is stored, read-only, rather than nothing at all. It is also the plain UI
    /// R13 names as the fallback if Phase 6 is cut back to its acceptance criteria.
    /// </remarks>
    public static Type Fallback { get; } = typeof(PlainZoneEditor);
}
