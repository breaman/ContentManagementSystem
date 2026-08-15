using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering;

/// <summary>
/// Base class for the component that renders one field type's stored value (spec section 15.2).
/// </summary>
/// <remarks>
/// One renderer per field type, resolved from the value's own <c>type</c> discriminator rather than
/// from the schema: a value has to be read by whatever wrote it, and a zone whose field type was
/// changed under stored content would otherwise be handed to a renderer that cannot read it.
/// <para>
/// The value arrives as raw JSON, exactly as it was stored, because there is no CLR type to bind it
/// to — zones and block properties are runtime data. Renderers must treat every member as
/// potentially absent, of the wrong kind, or authored against an older shape, and render nothing
/// rather than throw (spec section 15.3).
/// </para>
/// </remarks>
public abstract class CmsFieldRendererBase : ComponentBase
{
    /// <summary>
    /// The stored value: the whole property object, including its <c>type</c> discriminator.
    /// </summary>
    /// <remarks>
    /// An undefined element for a value that was never authored, and a null element for one an
    /// editor cleared. Both are ordinary and render nothing.
    /// </remarks>
    [Parameter]
    public JsonElement Value { get; set; }

    /// <summary>
    /// The key this value is stored under — a zone key, or a block property key.
    /// </summary>
    /// <remarks>
    /// Named for neither, because the two are the same thing at render time exactly as they are at
    /// validation time, and the same renderer serves both.
    /// </remarks>
    [Parameter]
    public string PropertyKey { get; set; } = string.Empty;

    /// <summary>
    /// The field configuration captured with the revision this content was authored against.
    /// </summary>
    /// <remarks>
    /// <see cref="FieldConfiguration.Empty"/> when the schema could not be resolved, or when it
    /// declares a different field type than the value says it is — configuration belonging to
    /// another field type is worse than none.
    /// </remarks>
    [Parameter]
    public FieldConfiguration Configuration { get; set; } = FieldConfiguration.Empty;

    /// <summary>The render context, cascaded by the delivery host.</summary>
    [CascadingParameter]
    public RenderContext Context { get; set; } = default!;

    /// <summary>
    /// The member the value-shaped field types store their content under.
    /// </summary>
    /// <remarks>
    /// Part of the on-disk contract, mirrored from <c>FieldTypeBase</c> rather than referenced,
    /// because the constant there is protected to its own hierarchy.
    /// </remarks>
    protected const string ValueMember = "value";

    /// <summary>The member the list-shaped field types store their items under.</summary>
    protected const string ItemsMember = "items";

    /// <summary>Whether there is a stored object to read at all.</summary>
    protected bool HasValue => Value.ValueKind is JsonValueKind.Object;

    /// <summary>The stored <c>value</c> member, or null when it is absent or was cleared.</summary>
    protected JsonElement? ValueElement => Member(ValueMember);

    /// <summary>The stored <c>value</c> member as text, or null when it is absent or is not a string.</summary>
    protected string? ValueText => StringMember(ValueMember);

    /// <summary>Reads a member of the stored value.</summary>
    /// <param name="name">The member name, such as <c>value</c> or <c>items</c>.</param>
    /// <returns>The member, or null when the value does not carry it.</returns>
    protected JsonElement? Member(string name) =>
        HasValue && Value.TryGetProperty(name, out var member) && member.ValueKind is not JsonValueKind.Null
            ? member
            : null;

    /// <summary>Reads a string member of the stored value.</summary>
    /// <param name="name">The member name.</param>
    /// <returns>The string, or null when the member is absent or is not a string.</returns>
    protected string? StringMember(string name) =>
        Member(name) is { ValueKind: JsonValueKind.String } member ? member.GetString() : null;

    /// <summary>Reads a list member of the stored value.</summary>
    /// <param name="name">The member name, usually <see cref="ItemsMember"/>.</param>
    /// <returns>The array, or null when the member is absent or holds something else.</returns>
    /// <remarks>
    /// A member that is present but is not an array reads as absent rather than as an error. The
    /// validator has already reported the shape; repeating it on the delivery path would only turn
    /// one editor-visible diagnostic into a log entry per request.
    /// </remarks>
    protected JsonElement? ArrayMember(string name) =>
        Member(name) is { ValueKind: JsonValueKind.Array } member ? member : null;

    /// <summary>Reads an integer identity member, such as a <c>mediaId</c> or a <c>pageId</c>.</summary>
    /// <param name="name">The member name.</param>
    /// <returns>The identity, or null when it is absent or is not a positive integer.</returns>
    /// <remarks>
    /// Positive is part of the question, not a separate check: every reference in the content model
    /// is a server-assigned identity, so a zero or a negative number is a value nothing can resolve
    /// and must not become a cache tag or an <c>href</c>.
    /// </remarks>
    protected int? IdMember(string name) =>
        Member(name) is { ValueKind: JsonValueKind.Number } member &&
        member.TryGetInt32(out var id) &&
        id > 0
            ? id
            : null;
}
