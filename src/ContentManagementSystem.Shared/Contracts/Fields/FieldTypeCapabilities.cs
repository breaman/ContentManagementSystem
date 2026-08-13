namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// What a field type can do, declared so the engine around it does not have to guess.
/// </summary>
/// <remarks>
/// These flags let the payload engine, the search indexer, and the contract tests treat field types
/// uniformly without special-casing keys. <see cref="Container"/> in particular is load-bearing: it
/// is how the reference-extraction contract test knows a field type has to be exercised with a
/// nested value, not just a flat one.
/// </remarks>
[Flags]
public enum FieldTypeCapabilities
{
    /// <summary>A plain value that stores no references and contributes nothing to search.</summary>
    None = 0,

    /// <summary>Contributes text to the search index through <c>ExtractSearchText</c>.</summary>
    Searchable = 1 << 0,

    /// <summary>May point at other entities, so <c>ExtractReferences</c> can return rows.</summary>
    ReferenceBearing = 1 << 1,

    /// <summary>Carries author-supplied markup that <c>SanitizeAsync</c> must clean.</summary>
    Sanitizable = 1 << 2,

    /// <summary>
    /// Holds values belonging to other field types, as <c>blocks</c> does.
    /// </summary>
    /// <remarks>
    /// A container must delegate back into the schema walk rather than inspecting its contents
    /// itself. One that does not silently drops every reference nested beneath it — the exact gap
    /// the S1 spike found, and the reason the contract test for
    /// <see cref="ReferenceBearing"/> has a second, nested case.
    /// </remarks>
    Container = 1 << 3,

    /// <summary>May participate in in-context editing on the rendered page (v2).</summary>
    InlineEditable = 1 << 4,

    /// <summary>
    /// Restricted to the <c>Developer</c> role by default, because its value reaches the page
    /// largely unmediated — <c>html</c> and <c>json</c>.
    /// </summary>
    DeveloperOnly = 1 << 5,
}
