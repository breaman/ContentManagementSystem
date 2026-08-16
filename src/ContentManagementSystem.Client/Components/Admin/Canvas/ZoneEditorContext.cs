using ContentManagementSystem.Shared.Contracts.Structure;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>
/// What a zone card hands to whatever draws the control inside it (task P6-05).
/// </summary>
/// <param name="Zone">The zone as the draft's template revision captured it.</param>
/// <param name="ControlId">
/// The id the body's control should carry. The card does not label it — see
/// <paramref name="LabelledBy"/> — but a body with one obvious control still wants a stable id, and
/// a deep link from the publish dialog (P6-20) needs somewhere to land.
/// </param>
/// <param name="LabelledBy">
/// Id of the card's heading. A body's control names itself with <c>aria-labelledby</c> pointing at
/// this rather than the card using <c>&lt;label for&gt;</c>: a block list or a media picker is
/// several controls and a label pointing at one of them is a lie, while a heading names all of them.
/// </param>
/// <param name="DescribedBy">
/// Id of the card's help text, or null when the zone has none. Bodies pass it straight to
/// <c>aria-describedby</c>, so the help an editor can see is also the help they can hear.
/// </param>
/// <param name="Disabled">Whether the surrounding form is read-only.</param>
/// <param name="Severity">
/// The worst thing validation said about this zone, so a body can mark itself invalid as well —
/// <c>aria-invalid</c> on the control is what a screen reader user gets instead of the badge.
/// </param>
/// <remarks>
/// The canvas deliberately does not resolve the control itself. ADR-0014 puts field-type components
/// behind a catalog keyed on the field type, and that catalog is built with the editors it maps
/// (P6-06 onwards) — inventing its parameter contract here, before a block list or a rich-text
/// editor exists to constrain it, would mean guessing at the shape of everything it has to carry.
/// What the canvas owns is the card: its order, its heading, its help text, its validation state,
/// and the ids everything inside it hangs off.
/// </remarks>
public sealed record ZoneEditorContext(
    CapturedSlot Zone,
    string ControlId,
    string LabelledBy,
    string? DescribedBy,
    bool Disabled,
    ZoneSeverity Severity)
{
    /// <summary>
    /// The value of <c>aria-invalid</c> for the body's control, or null when nothing is wrong.
    /// </summary>
    /// <remarks>
    /// Null rather than <c>"false"</c> so the attribute is omitted entirely on a healthy zone, which
    /// is the same thing to a screen reader and one less attribute in the diff of a rendered card.
    /// </remarks>
    public string? AriaInvalid => Severity is ZoneSeverity.Error ? "true" : null;
}
