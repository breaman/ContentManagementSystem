namespace ContentManagementSystem.Shared.Content;

/// <summary>
/// Whether a payload member was never authored, explicitly cleared, or holds a value.
/// </summary>
/// <remarks>
/// The distinction spec section 6.2 insists on, made explicit so calling code cannot lose it by
/// accident. <see cref="Absent"/> means the zone has never been touched — a zone added to the
/// template after this content was written reads this way, and must render as empty rather than as
/// deliberately blank. <see cref="Cleared"/> means an editor emptied it on purpose.
/// <para>
/// The two are indistinguishable once a payload is bound to a CLR model with nullable members, which
/// is why <see cref="ContentPayload"/> stays on <see cref="System.Text.Json.JsonElement"/>.
/// </para>
/// </remarks>
public enum ContentValueState
{
    /// <summary>The member is not present in the payload at all: never authored.</summary>
    Absent = 0,

    /// <summary>The member is present and null: authored, then explicitly cleared.</summary>
    Cleared = 1,

    /// <summary>The member is present and holds a value.</summary>
    Present = 2,
}
