using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Pages;

/// <summary>
/// The version history and the diff viewer that hangs off it (task P2-23).
/// </summary>
/// <remarks>
/// Two radio columns rather than a pair of dropdowns: the comparison a person actually wants is
/// almost always "this one against the one above it", and picking two rows out of a table they are
/// already reading is fewer steps than translating version numbers into a select.
/// </remarks>
public partial class PageVersions : ComponentBase
{
    /// <summary>Identity of the page whose history this is, from the route.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>Reads and writes pages, over HTTP in the browser and directly on the server.</summary>
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>The history, newest first, or null while loading.</summary>
    [PersistentState]
    public IReadOnlyList<PageVersionSummary>? Versions { get; set; }

    /// <summary>The earlier of the two versions being compared.</summary>
    private int? FromVersionId { get; set; }

    /// <summary>The later of the two versions being compared.</summary>
    private int? ToVersionId { get; set; }

    /// <summary>The last comparison, or null when none has been asked for.</summary>
    private ContentDiff? Diff { get; set; }

    /// <summary>Why the last action did not happen.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>A short confirmation of the last successful action.</summary>
    private string? Notice { get; set; }

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Whether two distinct versions are selected.</summary>
    private bool CanCompare =>
        FromVersionId is not null && ToVersionId is not null && FromVersionId != ToVersionId;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Versions ??= await Client.GetVersionsAsync(Id);

        // Preselect the published version against the draft, which is the comparison an editor
        // opening this screen is nearly always here to make: "what would publishing change?"
        FromVersionId ??= Versions.FirstOrDefault(version => version.IsPublished)?.Id;
        ToVersionId ??= Versions.FirstOrDefault(version => version.IsDraft)?.Id;
    }

    private async Task CompareAsync()
    {
        if (!CanCompare) return;

        IsBusy = true;
        Errors = null;
        Notice = null;

        try
        {
            Diff = await Client.GetDiffAsync(Id, FromVersionId!.Value, ToVersionId!.Value);

            if (Diff is null)
            {
                Errors = [new ApiDiagnostic(
                    PageCodes.VersionNotFound,
                    "One of those versions is no longer there. Reload the history.")];
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RestoreAsync(int versionId)
    {
        IsBusy = true;
        Errors = null;
        Notice = null;

        try
        {
            var result = await Client.RestoreVersionAsync(Id, versionId);

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Notice = "Copied into the draft. The published version is untouched — publish when ready.";

            // The draft's content changed, so both the history and any diff on screen are stale.
            Versions = await Client.GetVersionsAsync(Id);
            Diff = null;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
