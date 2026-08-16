using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Canvas;

/// <summary>
/// The middle pane of the backoffice shell: a page's zones as cards (task P6-05, spec section 14.1).
/// </summary>
/// <remarks>
/// The canvas owns four things and deliberately nothing else — the order the cards appear in, the
/// groups they sit under, the validation state each one shows, and the action bar pinned to the
/// bottom of the pane. What fills a card comes from <see cref="Editor"/>, and what the buttons do
/// comes from <see cref="Actions"/>: publishing is a workflow with a dialog (P6-20) and a permission
/// behind it, and a layout component that also decided when a page may go live would be the piece
/// nobody could reuse for reusable content.
/// <para>
/// <strong>Everything is built from the captured revision.</strong> The zones handed in are the ones
/// the draft was authored against, never the template's current ones (spec section 8.5) — including
/// their grouping, which is why <see cref="CapturedSlot"/> carries it. A canvas laid out from live
/// definitions would rearrange itself under an editor because somebody else regrouped the template.
/// </para>
/// </remarks>
public partial class EditingCanvas : ComponentBase
{
    /// <summary>The zones the draft's template revision captured.</summary>
    [Parameter]
    public IReadOnlyList<CapturedSlot>? Zones { get; set; }

    /// <summary>What the last validation said, sorted onto the cards it concerns.</summary>
    [Parameter]
    public CanvasDiagnostics Diagnostics { get; set; } = CanvasDiagnostics.Empty;

    /// <summary>Draws the control inside a zone card.</summary>
    [Parameter]
    public RenderFragment<ZoneEditorContext>? Editor { get; set; }

    /// <summary>Whether the whole canvas is read-only.</summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The buttons in the action bar. Nothing is assumed about what they do.</summary>
    [Parameter]
    public RenderFragment? Actions { get; set; }

    /// <summary>
    /// The save state, as "Saved 14:32" (spec section 14.1).
    /// </summary>
    /// <remarks>
    /// A string rather than a state machine, because the machine belongs to autosave (P6-18) and
    /// this is where its answer is shown.
    /// </remarks>
    [Parameter]
    public string? Status { get; set; }

    /// <summary>What to show when the template revision captured no zones at all.</summary>
    [Parameter]
    public RenderFragment? EmptyState { get; set; }

    /// <summary>Heading over the diagnostics that belong to no single zone.</summary>
    [Parameter]
    public string PageErrorHeading { get; set; } = "Blocking";

    /// <summary>The cards, grouped and ordered.</summary>
    private IReadOnlyList<CanvasGroup> Groups { get; set; } = [];

    /// <summary>Everything the cards will not show, reported above them instead.</summary>
    private ZoneDiagnostics Unplaced { get; set; } = ZoneDiagnostics.Empty;

    /// <summary>Whether there is anything to pin to the bottom of the pane.</summary>
    private bool HasActionBar =>
        Actions is not null || !string.IsNullOrWhiteSpace(Status) || Diagnostics.Any;

    private string SummaryClass =>
        Diagnostics.TotalErrors > 0 ? "text-bg-danger" : "text-bg-warning";

    /// <summary>
    /// What the action bar says about the last check, counted across the whole page.
    /// </summary>
    /// <remarks>
    /// The bar is where an editor looks before pressing publish, and the number is the part that
    /// tells them whether to expect a refusal. The cards themselves say <em>which</em> zones.
    /// </remarks>
    private string SummaryText => Diagnostics.TotalErrors switch
    {
        0 => Diagnostics.TotalWarnings == 1 ? "1 warning" : $"{Diagnostics.TotalWarnings} warnings",
        1 => "1 problem blocks publishing",
        var errors => $"{errors} problems block publishing",
    };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Groups = CanvasLayout.Build(Zones);

        Unplaced = Diagnostics.Unplaced(
            [.. Groups.SelectMany(group => group.Zones).Select(zone => zone.Key)]);
    }

    /// <summary>The id of a group's heading, or null for the run that has none.</summary>
    private static string? GroupLabelId(CanvasGroup group) =>
        group.Name is { Length: > 0 } name ? $"cms-canvas-group-{Slug(name)}" : null;

    /// <summary>Turns a group's heading into something usable as an element id.</summary>
    /// <remarks>
    /// A group name is free text a developer typed into the template editor, so it can hold spaces,
    /// punctuation, and case that an <c>id</c> reference cannot survive round-tripping through.
    /// </remarks>
    private static string Slug(string name) =>
        string.Concat(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-'));

}
