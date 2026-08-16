using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>
/// One zone as a card on the editing canvas (task P6-05, spec section 14.3).
/// </summary>
/// <remarks>
/// The card is the header — name, help text, required marker, validation state — and the body is
/// somebody else's, which is the whole arrangement: field editors arrive one at a time through P6-06
/// to P6-15, and each of them should be a body that drops into this card rather than a screen that
/// re-invents the frame around it.
/// <para>
/// <strong>The card is a labelled region, not a form field.</strong> It carries the zone's name as a
/// heading and points the body at it with <c>aria-labelledby</c>; a <c>&lt;label for&gt;</c> would
/// have to choose one control, and half the field types are several.
/// </para>
/// </remarks>
public partial class ZoneCard : ComponentBase
{
    /// <summary>The zone as the draft's template revision captured it.</summary>
    [Parameter]
    [EditorRequired]
    public CapturedSlot Zone { get; set; } = default!;

    /// <summary>What the last validation said about this zone.</summary>
    [Parameter]
    public ZoneDiagnostics Diagnostics { get; set; } = ZoneDiagnostics.Empty;

    /// <summary>Whether the surrounding form is read-only.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>Draws the control that fills the zone.</summary>
    [Parameter]
    public RenderFragment<FieldEditorContext>? Editor { get; set; }

    /// <summary>
    /// The card's element id, which is also what a deep link to this zone targets.
    /// </summary>
    /// <remarks>
    /// The section rather than the control, and the section takes <c>tabindex="-1"</c> so following
    /// the link moves focus as well as the viewport. Landing on the heading is what tells somebody
    /// arriving from the publish dialog (P6-20) which zone they were sent to; landing inside an
    /// editor tells them nothing and starts them mid-value.
    /// </remarks>
    public string Anchor => AnchorFor(Zone.Key);

    /// <summary>Id of the heading everything in the card is named by.</summary>
    private string LabelId => $"{Anchor}-name";

    /// <summary>Id of the help text, or null when the zone has none.</summary>
    private string? HelpId =>
        string.IsNullOrWhiteSpace(Zone.Description) ? null : $"{Anchor}-help";

    /// <summary>What the card hands to its body.</summary>
    private FieldEditorContext Context => new(
        Zone,
        $"{Anchor}-control",
        LabelId,
        HelpId,
        Disabled,
        Diagnostics.Severity);

    private string SeverityClass => Diagnostics.Severity switch
    {
        ZoneSeverity.Error => "cms-canvas__zone--error",
        ZoneSeverity.Warning => "cms-canvas__zone--warning",
        _ => string.Empty,
    };

    private string BadgeClass => Diagnostics.Severity is ZoneSeverity.Error
        ? "text-bg-danger"
        : "text-bg-warning";

    /// <summary>
    /// The badge's text, which counts what it is reporting.
    /// </summary>
    /// <remarks>
    /// A word and a count, never a colour alone — the same rule the tree's status indicators follow
    /// (P6-39, spec section 28). The count is what makes the badge worth reading twice: "3 problems"
    /// on a collapsed twelve-block zone is the difference between one typo and an unfinished zone.
    /// </remarks>
    private string BadgeText => Diagnostics.Severity is ZoneSeverity.Error
        ? Count(Diagnostics.Errors.Count, "problem")
        : Count(Diagnostics.Warnings.Count, "warning");

    /// <summary>The id a deep link to a zone targets.</summary>
    /// <param name="zoneKey">The zone's payload key.</param>
    /// <returns>The card's element id.</returns>
    public static string AnchorFor(string zoneKey) => $"zone-{zoneKey}";

    private static string Count(int count, string noun) =>
        count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    /// <summary>
    /// Where inside the zone a diagnostic was found, or null when it is about the zone itself.
    /// </summary>
    /// <remarks>
    /// The zone's own segment is dropped: the card already says which zone this is, and repeating it
    /// on every line pushes the part that differs — the block index and the property — off the end of
    /// a narrow canvas.
    /// </remarks>
    private string? Within(ApiDiagnostic diagnostic)
    {
        if (diagnostic.Property is not { Length: > 0 } path)
        {
            return null;
        }

        var prefix = $"{ContentPayloadMembers.Zones}.{Zone.Key}";

        if (!path.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        return path.Length > prefix.Length ? path[prefix.Length..].TrimStart('.') : null;
    }
}
