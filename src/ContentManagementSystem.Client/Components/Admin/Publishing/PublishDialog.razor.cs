using ContentManagementSystem.Client.Components.Admin.Canvas;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Structure;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Publishing;

/// <summary>
/// The publish dialog: what is wrong, which zone each thing is in, and the one acknowledgement
/// warnings need (task P6-20, spec sections 14.6 and 22.2).
/// </summary>
/// <remarks>
/// Errors and warnings arrive as one flat list, each naming the payload path it was found at. This
/// dialog turns that back into the shape an editor works in — a zone at a time, named as the canvas
/// names it, with a link that takes them to the card. Anything naming no zone (a URL collision, a
/// missing meta description) is grouped under the page itself rather than dropped.
/// <para>
/// <strong>The acknowledgement is a decision, not a formality.</strong> It appears only when
/// warnings are the only thing left, is unticked every time the dialog opens, and reads back what is
/// being published past — a checkbox that survived between openings would let an editor acknowledge
/// warnings they have never seen.
/// </para>
/// </remarks>
public partial class PublishDialog : ComponentBase
{
    /// <summary>Whether the dialog is on screen.</summary>
    [Parameter]
    public bool IsOpen { get; set; }

    /// <summary>Whether the dry-run check is still running.</summary>
    [Parameter]
    public bool IsChecking { get; set; }

    /// <summary>Whether the publish itself is in flight.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Everything blocking the publish.</summary>
    [Parameter]
    public IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>Everything worth showing that does not block.</summary>
    [Parameter]
    public IReadOnlyList<ApiDiagnostic>? Warnings { get; set; }

    /// <summary>
    /// The zones the draft was authored against, so a group can be named rather than keyed.
    /// </summary>
    /// <remarks>
    /// "Hero banner" is what the editor sees on the card; <c>zones.heroBanner</c> is what the
    /// diagnostic carries. A dialog that shows the second makes the editor do the translation.
    /// </remarks>
    [Parameter]
    public IReadOnlyList<CapturedSlot>? Zones { get; set; }

    /// <summary>Raised with whether the editor acknowledged the warnings.</summary>
    [Parameter]
    public EventCallback<bool> OnPublish { get; set; }

    /// <summary>Raised when the editor closes the dialog without publishing.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>Raised with the zone key the editor asked to be taken to.</summary>
    [Parameter]
    public EventCallback<string> OnGoToZone { get; set; }

    /// <summary>Whether the editor has ticked the acknowledgement.</summary>
    private bool Acknowledged { get; set; }

    /// <summary>Whether the dialog was open on the previous render, so opening can reset it.</summary>
    private bool _wasOpen;

    private int ErrorCount => Errors?.Count ?? 0;

    private int WarningCount => Warnings?.Count ?? 0;

    private bool HasErrors => ErrorCount > 0;

    private bool HasWarnings => WarningCount > 0;

    /// <summary>Whether publishing is currently possible.</summary>
    /// <remarks>
    /// Errors are absolute. Warnings are a decision, and the button stays disabled until it has been
    /// made — a "publish anyway" that works without the tick would make the tick decoration.
    /// </remarks>
    private bool CanConfirm => !IsChecking && !HasErrors && (!HasWarnings || Acknowledged);

    private string ConfirmLabel => HasWarnings && !HasErrors ? "Publish anyway" : "Publish";

    /// <summary>What the dialog says about the count, before the detail.</summary>
    private string Summary => (ErrorCount, WarningCount) switch
    {
        (0, 1) => "One thing is worth checking before this goes live.",
        (0, var warnings) => $"{warnings} things are worth checking before this goes live.",
        (1, 0) => "One problem is stopping this from being published.",
        (var errors, 0) => $"{errors} problems are stopping this from being published.",
        (1, var warnings) =>
            $"One problem is stopping this from being published, and {Count(warnings, "thing")} worth checking.",
        var (errors, warnings) =>
            $"{errors} problems are stopping this from being published, and {Count(warnings, "thing")} worth checking.",
    };

    /// <summary>The diagnostics, grouped by the zone each one is about.</summary>
    private IReadOnlyList<PublishGroup> Groups { get; set; } = [];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (IsOpen && !_wasOpen)
        {
            // Every opening starts unacknowledged. The warnings may not be the ones that were
            // acknowledged last time, and consent to a list nobody is looking at is not consent.
            Acknowledged = false;
        }

        _wasOpen = IsOpen;

        Groups = Group();
    }

    /// <summary>Publishes, reporting whether the warnings were acknowledged.</summary>
    private Task ConfirmAsync() => OnPublish.InvokeAsync(Acknowledged);

    private void OnAcknowledgedChanged(ChangeEventArgs args) => Acknowledged = args.Value is true;

    /// <summary>Sends the editor to the zone a group is about, and closes the dialog.</summary>
    /// <remarks>
    /// Closing is the point. A deep link that left a modal covering the card it just scrolled to
    /// would be a link to something the editor still cannot see or type into.
    /// </remarks>
    private async Task GoToAsync(string? zoneKey)
    {
        if (zoneKey is not { Length: > 0 }) return;

        await OnGoToZone.InvokeAsync(zoneKey);
    }

    /// <summary>
    /// Sorts the flat diagnostics into one group per zone, in the canvas's own order.
    /// </summary>
    /// <remarks>
    /// The canvas's order rather than the diagnostics' arrival order, so the dialog reads down the
    /// page the way the page reads. Zones that are not in the captured revision, and diagnostics
    /// that name no zone at all, fall into the page group at the end — the same rule
    /// <see cref="CanvasDiagnostics"/> follows, for the same reason: a message with nowhere to go is
    /// more hidden after grouping than it was before.
    /// </remarks>
    private IReadOnlyList<PublishGroup> Group()
    {
        var entries = new List<PublishEntry>();

        entries.AddRange((Errors ?? []).Select(diagnostic => new PublishEntry(diagnostic, true)));
        entries.AddRange((Warnings ?? []).Select(diagnostic => new PublishEntry(diagnostic, false)));

        if (entries.Count == 0) return [];

        var order = new Dictionary<string, (int Index, string Name)>(StringComparer.Ordinal);

        foreach (var (zone, index) in (Zones ?? []).Select((zone, index) => (zone, index)))
        {
            order[zone.Key] = (index, zone.Name);
        }

        return
        [
            .. entries
                .GroupBy(entry => Placed(entry, order), StringComparer.Ordinal)
                .OrderBy(group => group.Key.Length == 0 ? int.MaxValue : Rank(group.Key, order))
                .Select(group => new PublishGroup(
                    group.Key.Length == 0 ? "page" : group.Key,
                    group.Key.Length == 0 ? null : group.Key,
                    group.Key.Length == 0
                        ? "This page"
                        : order.TryGetValue(group.Key, out var known) ? known.Name : group.Key,
                    // Errors before warnings inside a group: the ones that stop a publish are what
                    // the editor came to read.
                    [.. group.OrderByDescending(entry => entry.IsError)]))
        ];
    }

    /// <summary>The zone a diagnostic belongs to, or the empty string for the page itself.</summary>
    private static string Placed(
        PublishEntry entry,
        Dictionary<string, (int Index, string Name)> order) =>
        CanvasDiagnostics.ZoneKeyOf(entry.Diagnostic.Property) is { } zoneKey && order.ContainsKey(zoneKey)
            ? zoneKey
            : string.Empty;

    private static int Rank(string zoneKey, Dictionary<string, (int Index, string Name)> order) =>
        order.TryGetValue(zoneKey, out var known) ? known.Index : int.MaxValue - 1;

    private static string Count(int count, string noun) =>
        count == 1 ? $"one {noun}" : $"{count} {noun}s";

    /// <summary>One diagnostic, and whether it blocks.</summary>
    /// <param name="Diagnostic">What was said.</param>
    /// <param name="IsError">Whether it stops the publish.</param>
    private sealed record PublishEntry(ApiDiagnostic Diagnostic, bool IsError);

    /// <summary>Everything said about one zone.</summary>
    /// <param name="Key">Usable as part of an element id.</param>
    /// <param name="ZoneKey">The zone to link to, or null for the page's own group.</param>
    /// <param name="Name">What the editor calls it.</param>
    /// <param name="Entries">What was said, blocking first.</param>
    private sealed record PublishGroup(
        string Key,
        string? ZoneKey,
        string Name,
        IReadOnlyList<PublishEntry> Entries);
}
