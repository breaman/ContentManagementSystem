using System.Diagnostics.CodeAnalysis;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <summary>
/// Maps a field type key to the component an author fills it in with (spec section 14.3,
/// ADR-0014).
/// </summary>
/// <remarks>
/// The backoffice's half of the arrangement <see cref="ContentManagementSystem.Shared.Contracts.Fields.IFieldType"/>
/// describes. <c>IFieldType.EditorComponent</c> is <c>Type?</c> and every built-in field type answers
/// null, because <c>Core</c> sits below <c>Client</c> in the reference graph and cannot name a
/// component in it — so the mapping belongs to the layer that owns the components, exactly as
/// <c>IFieldRendererCatalog</c> owns the renderer half in <c>Rendering</c>.
/// <para>
/// <strong>A missing editor is not the same failure as a missing renderer.</strong> A field type with
/// no renderer costs a reader a paragraph; a field type with no editor leaves an author with no way
/// at all to fill a property their template marks required, and therefore no way to publish the page.
/// That is why <see cref="FallbackEditor"/> exists and is never null: the backoffice always draws
/// <em>something</em>, even for a field type this build has never heard of, and
/// <see cref="FieldTypesWithNoEditor"/> is what says so out loud at startup.
/// </para>
/// </remarks>
public interface IFieldEditorCatalog
{
    /// <summary>Every field type key that has an editor of its own.</summary>
    IReadOnlyCollection<string> FieldTypeKeys { get; }

    /// <summary>
    /// The registered field types that resolve to no editor at all and fall back.
    /// </summary>
    /// <remarks>
    /// Read at startup and reported. At editing time the condition is survivable — the fallback shows
    /// the stored value and writes it back untouched — but survivable is not the same as acceptable:
    /// a required property nobody can fill blocks every publish of every page using the template,
    /// and the person who can fix it is the one reading the deployment log, not the author.
    /// </remarks>
    IReadOnlyCollection<string> FieldTypesWithNoEditor { get; }

    /// <summary>
    /// What draws a field type this catalog has no entry for.
    /// </summary>
    /// <remarks>
    /// Never null. This is also R13's fallback: if Phase 6 is cut back to its acceptance criteria,
    /// the plain control that P1 to P5 shipped is still what fills every zone that lost its editor.
    /// </remarks>
    Type FallbackEditor { get; }

    /// <summary>Finds the editor for a field type.</summary>
    /// <param name="fieldTypeKey">The field type key on the slot, such as <c>richText</c>.</param>
    /// <param name="componentType">The editor component, when one is registered.</param>
    /// <returns><see langword="true"/> when an editor is registered for the key.</returns>
    bool TryGetEditor(string fieldTypeKey, [NotNullWhen(true)] out Type? componentType);

    /// <summary>The component that draws a field type, falling back when it has no editor.</summary>
    /// <param name="fieldTypeKey">The field type key on the slot.</param>
    /// <returns>The editor, or <see cref="FallbackEditor"/>.</returns>
    Type EditorFor(string fieldTypeKey);
}
