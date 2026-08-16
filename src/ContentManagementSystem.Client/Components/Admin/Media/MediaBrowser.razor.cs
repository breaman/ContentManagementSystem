using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace ContentManagementSystem.Client.Components.Admin.Media;

/// <summary>
/// Browses the media library — folders, filters, a grid of thumbnails, and the uploader
/// (tasks P5-08 and P5-22, spec section 13.8).
/// </summary>
/// <remarks>
/// One component behind two screens, because they are the same screen: the library page at
/// <c>/admin/media</c> and the picker a <c>media</c> field opens both need "find me a file", with
/// the same filters and the same recycle bin. Two copies would drift the first time either gained a
/// filter, and the one that drifted would be the picker — the one an editor uses most.
/// <para>
/// <strong>The thumbnails are fetched, not built.</strong> A client cannot sign a rendition URL, so
/// each page of results is followed by one batched request for the URLs of the items on it
/// (spec section 13.5). One request per page, never one per tile.
/// </para>
/// </remarks>
public partial class MediaBrowser : ComponentBase
{
    /// <summary>How many items one page of the grid holds.</summary>
    private const int PageSize = 24;

    /// <summary>
    /// Largest file the uploader accepts from the browser, in bytes.
    /// </summary>
    /// <remarks>
    /// Blazor's file API requires an explicit ceiling and defaults to 512 KB, which would refuse
    /// almost every photograph before the server saw it. This is the larger of the server's two
    /// limits; the server applies the exact one for the file's kind, and does so again on the bytes
    /// that arrive.
    /// </remarks>
    private const long MaxUploadBytes = 50L * 1024 * 1024;

    [Inject]
    private IMediaClient Client { get; set; } = default!;

    /// <summary>
    /// Raised when an editor chooses an item, when the caller is a picker rather than the library.
    /// </summary>
    /// <remarks>
    /// Unset on the library page, which is what makes the tiles links there and buttons in a picker.
    /// The component does not otherwise know which it is, and does not need to.
    /// </remarks>
    [Parameter]
    public EventCallback<MediaDetail> OnPicked { get; set; }

    /// <summary>Restrict the grid to one kind of file, as a <c>media</c> field's picker does.</summary>
    [Parameter]
    public string? RestrictToKind { get; set; }

    /// <summary>Whether the recycle bin and its restore action are offered.</summary>
    [Parameter]
    public bool AllowRecycleBin { get; set; } = true;

    /// <summary>The page of items currently shown, or null while loading.</summary>
    private MediaListResult? Items { get; set; }

    /// <summary>Signed URLs for the items on this page, keyed by item id.</summary>
    private IReadOnlyDictionary<int, MediaLinks> Links { get; set; } = new Dictionary<int, MediaLinks>();

    /// <summary>The folder tree, or null while loading.</summary>
    private IReadOnlyList<MediaFolderNode>? Folders { get; set; }

    /// <summary>Folder being listed, or null for the whole library.</summary>
    private int? FolderId { get; set; }

    /// <summary>Free-text filter over name, title, and alternative text.</summary>
    private string? Search { get; set; }

    /// <summary>Kind filter, or null for every kind.</summary>
    private string? Kind { get; set; }

    /// <summary>Whether only items nothing points at are shown.</summary>
    private bool UnusedOnly { get; set; }

    /// <summary>Whether the recycle bin is being listed instead of the live library.</summary>
    private bool ShowBin { get; set; }

    /// <summary>How many items are skipped, which is the pager's whole state.</summary>
    private int Skip { get; set; }

    /// <summary>Name of the folder being created.</summary>
    private string? NewFolderName { get; set; }

    /// <summary>What the last write refused to do.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>What the last upload reported without refusing.</summary>
    private string? Notice { get; set; }

    /// <summary>Fraction of the current upload the server has confirmed, or null when idle.</summary>
    private double? UploadProgress { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Alternative text typed for the file about to be uploaded.</summary>
    private string? UploadAltText { get; set; }

    /// <summary>Whether the file about to be uploaded is decorative.</summary>
    private bool UploadIsDecorative { get; set; }

    /// <summary>Whether the grid is showing the whole library or one folder's contents.</summary>
    private string FolderLabel => FolderId is null ? "All folders" : FindFolder(Folders, FolderId.Value)?.Name ?? "Folder";

    /// <summary>Whether there is a page after this one.</summary>
    private bool HasMore => Items is { } page && page.Skip + page.Items.Count < page.Total;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        Kind ??= RestrictToKind;

        Folders = await Client.FoldersAsync();

        await ReloadAsync();
    }

    /// <summary>Loads the current page and the signed URLs for what is on it.</summary>
    private async Task ReloadAsync()
    {
        Items = await Client.ListAsync(new MediaQuery
        {
            FolderId = FolderId,
            Kind = RestrictToKind ?? Kind,
            Search = Search,
            UnusedOnly = UnusedOnly,
            DeletedOnly = ShowBin,
            Skip = Skip,
            Take = PageSize,
        });

        Links = await Client.LinksAsync(Items.Items.Select(item => item.Id));
    }

    /// <summary>Applies a changed filter, which always returns to the first page.</summary>
    /// <remarks>
    /// Resetting <see cref="Skip"/> is not a nicety: a filter applied on page four of an unfiltered
    /// list would otherwise show page four of a result set that may have three pages, which reads as
    /// "no results" for a search that matched.
    /// </remarks>
    private async Task FilterChangedAsync()
    {
        Skip = 0;

        await ReloadAsync();
    }

    private async Task SelectFolderAsync(int? folderId)
    {
        FolderId = folderId;

        await FilterChangedAsync();
    }

    private async Task PageAsync(int delta)
    {
        Skip = Math.Max(0, Skip + (delta * PageSize));

        await ReloadAsync();
    }

    private async Task CreateFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(NewFolderName)) return;

        await GuardedAsync(async () =>
        {
            var created = await Client.CreateFolderAsync(new CreateMediaFolderRequest(NewFolderName, FolderId));

            if (!created.IsSuccess)
            {
                Errors = created.Errors;

                return;
            }

            NewFolderName = null;
            Folders = await Client.FoldersAsync();
        });
    }

    /// <summary>
    /// Uploads the files an editor chose, one after another, reporting the server's own progress.
    /// </summary>
    /// <param name="args">The chosen files.</param>
    /// <remarks>
    /// Sequential rather than parallel. Uploads are the one thing in this backoffice that saturate a
    /// connection, and six at once make every one of them slower and the progress meaningless — while
    /// one at a time lets an editor watch the queue drain and stop it partway with nothing half-done.
    /// <para>
    /// The stream is handed to the client without being buffered here. Which transport it takes —
    /// one request or a resumable sequence of parts — is the client's decision, made from the size
    /// the browser reports (task P5-08).
    /// </para>
    /// </remarks>
    private async Task UploadAsync(InputFileChangeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        await GuardedAsync(async () =>
        {
            var uploaded = 0;
            var deduplicated = 0;

            foreach (var file in args.GetMultipleFiles(maximumFileCount: 20))
            {
                UploadProgress = 0;

                var progress = new Progress<double>(fraction =>
                {
                    UploadProgress = fraction;
                    StateHasChanged();
                });

                await using var content = file.OpenReadStream(MaxUploadBytes);

                var result = await Client.UploadAsync(
                    new MediaUploadContent(
                        content,
                        file.Name,
                        file.Size,
                        FolderId,
                        UploadAltText,
                        UploadIsDecorative),
                    progress);

                if (!result.IsSuccess)
                {
                    // Reported against the file that failed, and the loop stops: the remaining files
                    // were chosen in the same gesture, and pushing on would bury the refusal under
                    // however many succeeded after it.
                    Errors = result.Errors;

                    break;
                }

                if (result.Value!.Deduplicated) deduplicated++;
                else uploaded++;
            }

            UploadProgress = null;
            UploadAltText = null;
            UploadIsDecorative = false;

            Notice = Describe(uploaded, deduplicated);

            await ReloadAsync();
        });
    }

    /// <summary>
    /// Says what an upload did, including the part an editor would otherwise find confusing.
    /// </summary>
    /// <remarks>
    /// Deduplication is reported rather than hidden. An editor who uploads a file and is shown an
    /// item with a different name, in a different folder, needs to be told it is the same picture
    /// they added in March — the alternative reads as an upload that landed somewhere unexpected
    /// (spec section 13.1).
    /// </remarks>
    private static string? Describe(int uploaded, int deduplicated) => (uploaded, deduplicated) switch
    {
        (0, 0) => null,
        (_, 0) => $"Uploaded {uploaded} file{(uploaded == 1 ? string.Empty : "s")}.",
        (0, _) => $"{deduplicated} file{(deduplicated == 1 ? " was" : "s were")} already in the library; " +
            "the existing item is shown rather than a copy.",
        _ => $"Uploaded {uploaded}; {deduplicated} " +
            $"{(deduplicated == 1 ? "was" : "were")} already in the library.",
    };

    /// <summary>
    /// The alternative text a grid thumbnail carries.
    /// </summary>
    /// <param name="item">The item the tile shows.</param>
    /// <returns>The library's description, or the empty string. Never null.</returns>
    /// <remarks>
    /// Never null is the point. Blazor omits an attribute whose value is null, and an
    /// <c>&lt;img&gt;</c> with no <c>alt</c> at all is announced by its file name — which here is a
    /// content-addressed hex string. The tile's own text names the file, so an undescribed item's
    /// thumbnail is presentational and says so.
    /// </remarks>
    private static string Describe(MediaDetail item) =>
        item.IsDecorative || string.IsNullOrWhiteSpace(item.AltText) ? string.Empty : item.AltText;

    private Task PickAsync(MediaDetail item) => OnPicked.InvokeAsync(item);

    /// <summary>Runs a write with the busy flag, the error slot, and the notice all reset first.</summary>
    private async Task GuardedAsync(Func<Task> write)
    {
        IsBusy = true;
        Errors = null;
        Notice = null;

        try
        {
            await write();
        }
        finally
        {
            IsBusy = false;
            UploadProgress = null;
        }
    }

    /// <summary>Finds a folder anywhere in the tree.</summary>
    private static MediaFolderNode? FindFolder(IReadOnlyList<MediaFolderNode>? nodes, int id)
    {
        foreach (var node in nodes ?? [])
        {
            if (node.Id == id) return node;

            if (FindFolder(node.Children, id) is { } found) return found;
        }

        return null;
    }
}
