using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ContentManagementSystem.Client.Components.Admin.RecycleBin;

/// <summary>
/// The recycle bin of spec section 14.10 (task P6-28).
/// </summary>
/// <remarks>
/// Two things about this screen are load-bearing rather than decorative.
/// <para>
/// <strong>It lists subtree roots, not deleted rows.</strong> Deleting a section deletes everything
/// under it, and a bin that showed all forty rows would ask an editor to restore them one at a time
/// — in an order that matters, since a child restored before its parent comes back at the site root.
/// The roots are what was deleted; the count beside each one is what goes with it.
/// </para>
/// <para>
/// <strong>Permanent deletion asks for the name to be typed.</strong> It is the one operation in the
/// system with no undo and no history left behind, so the ceremony is the feature. It is also
/// Administrator-only — the button is absent, not disabled, for anybody else — and the server
/// refuses it while stored content still points at the page, naming the pages in the way rather
/// than counting them.
/// </para>
/// </remarks>
public partial class RecycleBinScreen : ComponentBase
{
    /// <summary>Reads the bin, restores from it, and empties it.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Renders "deleted 3 days ago" against the same clock the rest of the backoffice reads.</summary>
    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>Everything in the bin, or null while it is still loading.</summary>
    [PersistentState]
    public IReadOnlyList<RecycleBinEntry>? Entries { get; set; }

    /// <summary>What the last write refused to do.</summary>
    private IReadOnlyList<ApiDiagnostic>? _errors;

    /// <summary>What the last restore did anyway, worth saying — usually a parent still deleted.</summary>
    private IReadOnlyList<ApiDiagnostic>? _warnings;

    /// <summary>The entry a permanent delete is being confirmed for, or null when none is.</summary>
    private RecycleBinEntry? _purging;

    /// <summary>What the editor has typed into the confirmation box.</summary>
    private string _typedName = string.Empty;

    /// <summary>The filter term, matched against title, slug, and id.</summary>
    private string _filter = string.Empty;

    /// <summary>Whether a write is in flight.</summary>
    private bool _working;

    /// <summary>What a screen reader is told after a restore or a purge.</summary>
    private string? _announcement;

    /// <summary>Ties the filter box to its own label.</summary>
    private string FilterId { get; } = $"bin-filter-{Guid.NewGuid():n}";

    /// <summary>Ties the typed-name box to its own label.</summary>
    private string ConfirmId { get; } = $"bin-confirm-{Guid.NewGuid():n}";

    /// <summary>
    /// The entries the table shows: subtree roots, most recently deleted first, matching the filter.
    /// </summary>
    /// <remarks>
    /// Filtering by id as well as by text is the same decision the tree's filter made: an editor
    /// arriving from a log line or a ticket is holding a number, not a title.
    /// </remarks>
    private IReadOnlyList<RecycleBinEntry> Roots =>
    [
        .. (Entries ?? [])
            .Where(entry => entry.IsSubtreeRoot)
            .Where(Matches),
    ];

    /// <summary>Whether the typed name matches the page being destroyed.</summary>
    /// <remarks>
    /// Compared case-insensitively and trimmed. The point of the box is that somebody read the name
    /// and typed it, not that they reproduced its capitalisation.
    /// </remarks>
    private bool IsNameTyped =>
        _purging is { } entry &&
        string.Equals(_typedName.Trim(), entry.Title.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Entries ??= await Client.GetRecycleBinAsync();
    }

    /// <summary>Whether an entry matches the filter.</summary>
    private bool Matches(RecycleBinEntry entry)
    {
        if (string.IsNullOrWhiteSpace(_filter)) return true;

        var term = _filter.Trim();

        return entry.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Slug.Contains(term, StringComparison.OrdinalIgnoreCase) ||
            entry.Id.ToString(CultureInfo.InvariantCulture) == term;
    }

    /// <summary>Narrows the list as the editor types.</summary>
    /// <remarks>
    /// Not debounced, and deliberately: the bin is already in memory, so the filter is a predicate
    /// over a list rather than a request. The tree's filter is debounced because it is a search.
    /// </remarks>
    private void OnFilterChanged(ChangeEventArgs args) => _filter = args.Value?.ToString() ?? string.Empty;

    /// <summary>Records what has been typed into the confirmation box.</summary>
    private void OnTypedNameChanged(ChangeEventArgs args) =>
        _typedName = args.Value?.ToString() ?? string.Empty;

    /// <summary>Brings a page and its subtree back, as drafts.</summary>
    private async Task RestoreAsync(RecycleBinEntry entry)
    {
        _working = true;
        _errors = null;
        _warnings = null;

        try
        {
            var result = await Client.RestoreAsync(entry.Id);

            if (!result.IsSuccess)
            {
                _errors = result.Errors;

                return;
            }

            // The warnings come from the result rather than from the entry, because the one that
            // matters — "its parent is still deleted, so it came back at the site root" — is only
            // knowable by the restore that just happened.
            _warnings = result.Warnings.Count > 0 ? result.Warnings : result.Value!.Warnings;

            var restored = result.Value!.AffectedPageIds.Count;

            _announcement = restored == 1
                ? $"“{entry.Title}” was restored as a draft."
                : $"“{entry.Title}” and {restored - 1} page(s) beneath it were restored as drafts.";

            await ReloadAsync();
        }
        finally
        {
            _working = false;
        }
    }

    /// <summary>Opens the typed-name confirmation.</summary>
    private void AskToPurge(RecycleBinEntry entry)
    {
        _purging = entry;
        _typedName = string.Empty;
        _errors = null;
        _warnings = null;
    }

    /// <summary>Backs out of a permanent delete.</summary>
    private void CancelPurge()
    {
        _purging = null;
        _typedName = string.Empty;
    }

    /// <summary>Sends the confirmed permanent delete.</summary>
    private async Task ConfirmPurgeAsync()
    {
        if (_purging is not { } entry || !IsNameTyped) return;

        _working = true;

        try
        {
            var result = await Client.PurgeAsync(entry.Id);

            if (!result.IsSuccess)
            {
                // The dialog closes on a refusal. What blocked it — content elsewhere still pointing
                // at this page — is not something to fix from inside a confirmation box, and leaving
                // the box open over a message it cannot act on invites typing the name again.
                _errors = result.Errors;
                _purging = null;

                return;
            }

            _purging = null;
            _announcement =
                $"“{entry.Title}” was permanently deleted, with {result.Value!.VersionsRemoved} version(s).";

            await ReloadAsync();
        }
        finally
        {
            _working = false;
        }
    }

    /// <summary>Re-reads the bin after a write.</summary>
    /// <remarks>
    /// The whole list, not one row: restoring a section takes its descendants out of the bin too,
    /// and a purge can turn a page that was somebody's descendant into a root of its own.
    /// </remarks>
    private async Task ReloadAsync()
    {
        Entries = await Client.GetRecycleBinAsync();
    }

    /// <summary>How long ago an entry was deleted, phrased the way an editor would say it.</summary>
    private string Deleted(RecycleBinEntry entry)
    {
        if (entry.DeletedOn is not { } deletedOn) return "Unknown";

        var elapsed = Clock.GetUtcNow() - deletedOn;

        return elapsed switch
        {
            { TotalMinutes: < 1 } => "Just now",
            { TotalHours: < 1 } => $"{(int)elapsed.TotalMinutes} minute(s) ago",
            { TotalDays: < 1 } => $"{(int)elapsed.TotalHours} hour(s) ago",
            _ => $"{(int)elapsed.TotalDays} day(s) ago",
        };
    }
}
