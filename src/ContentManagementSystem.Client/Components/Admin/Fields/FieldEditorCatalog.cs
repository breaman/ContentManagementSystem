using System.Diagnostics.CodeAnalysis;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <inheritdoc />
/// <remarks>
/// Built from the built-in table, and — where the caller can say — checked against the field types
/// this deployment actually registered. Those are two different questions and the catalog answers
/// both: <see cref="FieldTypeKeys"/> is what the backoffice can draw, and
/// <see cref="FieldTypesWithNoEditor"/> is what it has been asked to draw and cannot.
/// <para>
/// <strong>Only the table is consulted, never <c>IFieldType.EditorComponent</c>.</strong> ADR-0014
/// has resolution check the catalog first and the field type second, and the second half is
/// unreachable from here on purpose: the backoffice runs in WebAssembly, where <c>Core</c> is not
/// loaded and a <see cref="Type"/> it named could not be resolved anyway. A field type shipped in an
/// assembly that <em>can</em> see <c>Client</c> registers its editor through
/// <see cref="For(string[])"/>'s overload rather than through the interface property.
/// </para>
/// </remarks>
public sealed class FieldEditorCatalog : IFieldEditorCatalog
{
    private readonly Dictionary<string, Type> _editors;

    private readonly string[] _missing;

    /// <summary>
    /// Builds the catalog over the editors this build ships, with nothing to check them against.
    /// </summary>
    /// <remarks>
    /// The browser's constructor. The registered field types are a server-side fact the backoffice
    /// learns over HTTP, well after its services are built, so in the browser
    /// <see cref="FieldTypesWithNoEditor"/> is empty and the startup check is the server's — see
    /// <c>CmsEditorStartupService</c>, which is the only place both halves are in scope at once.
    /// </remarks>
    public FieldEditorCatalog()
        : this(BuiltInFieldEditors.ByFieldTypeKey, [])
    {
    }

    private FieldEditorCatalog(IReadOnlyDictionary<string, Type> editors, IEnumerable<string> registered)
    {
        _editors = new Dictionary<string, Type>(editors, StringComparer.Ordinal);

        foreach (var (key, editor) in _editors)
        {
            if (typeof(IComponent).IsAssignableFrom(editor)) continue;

            // At construction rather than at render, for the reason FieldRendererCatalog refuses the
            // same mistake: rendering it would fail one zone at a time, in production, on whichever
            // page first reached content using that field type.
            throw new InvalidOperationException(
                $"Field type '{key}' is mapped to '{editor.FullName}' as its editor, but that is " +
                "not a Razor component.");
        }

        var missing = registered.Where(key => !_editors.ContainsKey(key)).ToList();

        missing.Sort(StringComparer.Ordinal);
        _missing = [.. missing];
    }

    /// <summary>
    /// Builds the catalog and reports which of the given field types it cannot draw.
    /// </summary>
    /// <param name="registeredFieldTypeKeys">Keys of the field types this deployment registered.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidOperationException">An entry is not a Razor component.</exception>
    /// <remarks>
    /// A factory rather than a second public constructor, following <c>FieldRendererCatalog</c>: a
    /// constructor taking <c>IEnumerable&lt;string&gt;</c> alongside a parameterless one is the kind
    /// of pair a container resolves by picking whichever it likes, and the failure surfaces as a
    /// message about constructors rather than about editors.
    /// </remarks>
    public static FieldEditorCatalog For(params string[] registeredFieldTypeKeys) =>
        new(BuiltInFieldEditors.ByFieldTypeKey, registeredFieldTypeKeys ?? []);

    /// <summary>
    /// Builds the catalog over an explicit editor table, checked against the registered field types.
    /// </summary>
    /// <param name="editors">The field type key to editor mapping.</param>
    /// <param name="registeredFieldTypeKeys">Keys of the field types this deployment registered.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="InvalidOperationException">An entry is not a Razor component.</exception>
    /// <remarks>
    /// The extension point ADR-0014 promises: a deployment can replace the editor of a field type it
    /// did not write without reimplementing the field type. It is also how a test states exactly
    /// which editors exist and still exercises the resolution a real backoffice runs.
    /// </remarks>
    public static FieldEditorCatalog For(
        IReadOnlyDictionary<string, Type> editors,
        IEnumerable<string> registeredFieldTypeKeys) =>
        new(
            editors ?? throw new ArgumentNullException(nameof(editors)),
            registeredFieldTypeKeys ?? []);

    /// <inheritdoc />
    public IReadOnlyCollection<string> FieldTypeKeys => _editors.Keys;

    /// <inheritdoc />
    public IReadOnlyCollection<string> FieldTypesWithNoEditor => _missing;

    /// <inheritdoc />
    public Type FallbackEditor => BuiltInFieldEditors.Fallback;

    /// <inheritdoc />
    public bool TryGetEditor(string fieldTypeKey, [NotNullWhen(true)] out Type? componentType)
    {
        componentType = null;

        return !string.IsNullOrEmpty(fieldTypeKey) && _editors.TryGetValue(fieldTypeKey, out componentType);
    }

    /// <inheritdoc />
    public Type EditorFor(string fieldTypeKey) =>
        TryGetEditor(fieldTypeKey, out var editor) ? editor : FallbackEditor;
}
