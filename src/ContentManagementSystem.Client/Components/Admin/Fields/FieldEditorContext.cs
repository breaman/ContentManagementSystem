using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin.Fields;

/// <summary>
/// What the frame around a field editor hands to whatever draws the control inside it
/// (tasks P6-05 and P6-06 onwards).
/// </summary>
/// <param name="Slot">The zone or block property as the captured revision recorded it.</param>
/// <param name="ControlId">
/// The id the body's control should carry. The frame does not label it — see
/// <paramref name="LabelledBy"/> — but a body with one obvious control still wants a stable id, and
/// a deep link from the publish dialog (P6-20) needs somewhere to land.
/// </param>
/// <param name="LabelledBy">
/// Id of the frame's heading. A body's control names itself with <c>aria-labelledby</c> pointing at
/// this rather than the frame using <c>&lt;label for&gt;</c>: a block list or a media picker is
/// several controls and a label pointing at one of them is a lie, while a heading names all of them.
/// </param>
/// <param name="DescribedBy">
/// Id of the frame's help text, or null when the slot has none. Bodies pass it straight to
/// <c>aria-describedby</c>, so the help an editor can see is also the help they can hear.
/// </param>
/// <param name="Disabled">Whether the surrounding form is read-only.</param>
/// <param name="Severity">
/// The worst thing validation said about this slot, so a body can mark itself invalid as well —
/// <c>aria-invalid</c> on the control is what a screen reader user gets instead of the badge.
/// </param>
/// <param name="Diagnostics">
/// Everything validation said about this slot and the values inside it, so a container editor can
/// sort them onto the items they name. A leaf editor ignores them: the frame already prints them.
/// </param>
/// <param name="PayloadPath">
/// Where this slot sits in the payload — <c>zones.hero</c> at the top, and
/// <c>zones.body.items[0].properties.title</c> for a block property — which is the prefix every
/// diagnostic inside it carries.
/// </param>
/// <remarks>
/// <strong>One context for zones and for block properties alike.</strong> A zone card and a block's
/// property row are the same thing to an editor component: a labelled region, a stored value, and a
/// callback. The block list (P6-06) builds one of these per property of every block it draws, which
/// is what lets a rich-text editor inside a hero banner be the identical component to one filling a
/// zone rather than a second implementation that drifts from it.
/// <para>
/// Nothing has to be translated at that boundary, because a block type revision's captured property
/// snapshot and a template revision's captured zone snapshot are written by the same code and read
/// into the same <see cref="CapturedSlot"/> — see <c>ContentSchemaSnapshot</c>.
/// </para>
/// </remarks>
public sealed record FieldEditorContext(
    CapturedSlot Slot,
    string ControlId,
    string LabelledBy,
    string? DescribedBy,
    bool Disabled,
    ZoneSeverity Severity,
    ZoneDiagnostics? Diagnostics = null,
    string? PayloadPath = null)
{
    /// <summary>
    /// The value of <c>aria-invalid</c> for the body's control, or null when nothing is wrong.
    /// </summary>
    /// <remarks>
    /// Null rather than <c>"false"</c> so the attribute is omitted entirely on a healthy slot, which
    /// is the same thing to a screen reader and one less attribute in the diff of a rendered card.
    /// </remarks>
    public string? AriaInvalid => Severity is ZoneSeverity.Error ? "true" : null;

    /// <summary>The field type key filling the slot.</summary>
    public string FieldTypeKey => Slot.FieldTypeKey;

    /// <summary>
    /// Where this slot sits in the payload, falling back to the zone path when nobody said.
    /// </summary>
    /// <remarks>
    /// The fallback keeps every construction site that predates nesting — the canvas's, and every
    /// test that builds a context for a leaf editor — producing the path those slots actually have.
    /// </remarks>
    public string Path => PayloadPath ?? $"{ContentPayloadMembers.Zones}.{Slot.Key}";

    /// <summary>
    /// Builds the context for a value nested inside this one.
    /// </summary>
    /// <param name="slot">The nested slot, from the container's own captured schema.</param>
    /// <param name="controlId">Id for the nested control, unique within the document.</param>
    /// <param name="labelledBy">Id of the heading naming the nested control.</param>
    /// <param name="path">Full payload path of the nested value.</param>
    /// <param name="describedBy">Id of the nested help text, or null when it has none.</param>
    /// <returns>The nested context, carrying only the diagnostics that name something inside it.</returns>
    /// <remarks>
    /// The severity is recomputed from the diagnostics that survive the narrowing rather than
    /// inherited. A block list with one bad block is an error at the zone, but the eleven blocks
    /// beside it are fine, and marking all twelve <c>aria-invalid</c> would tell a screen reader user
    /// to go and check every one of them.
    /// </remarks>
    public FieldEditorContext Nested(
        CapturedSlot slot,
        string controlId,
        string labelledBy,
        string path,
        string? describedBy = null)
    {
        ArgumentNullException.ThrowIfNull(slot);

        var nested = (Diagnostics ?? ZoneDiagnostics.Empty).Within(path);

        return new FieldEditorContext(
            slot,
            controlId,
            labelledBy,
            describedBy,
            Disabled,
            nested.Severity,
            nested,
            path);
    }
}
