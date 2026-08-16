using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace ContentManagementSystem.Client.Components.Admin.Media;

/// <summary>
/// One media item: its metadata, its library-scope edits, where it is used, and its lifecycle
/// (task P5-22, spec sections 13.4, 13.7 and 13.8).
/// </summary>
/// <remarks>
/// Four things an editor does to a file, kept on one screen because they are the same decision seen
/// from different angles: describe it, straighten it, find out who is relying on it, and get rid of
/// it. The where-used list in particular belongs beside the delete button rather than behind a
/// second click — it is the answer to the question the button raises.
/// <para>
/// <strong>The image editor is numeric, and that is the honest Phase 5 answer.</strong> The
/// operations and their storage are complete — rotate, flip, a normalized crop, a focal point, all
/// applied to renditions and none of them touching the uploaded bytes — and a drag-and-drop crop
/// surface is authoring experience, which is Phase 6. What is here does everything the model can do;
/// the preview above it shows the result of the last save, because a preview of unsaved geometry
/// would need a rendition nobody has signed.
/// </para>
/// </remarks>
public partial class MediaItemEditor : ComponentBase
{
    /// <summary>Largest replacement file accepted from the browser, matching the browser's ceiling.</summary>
    private const long MaxUploadBytes = 50L * 1024 * 1024;

    [Inject]
    private IMediaClient Client { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>Identity of the item, from the route.</summary>
    [Parameter]
    public int Id { get; set; }

    /// <summary>The item as the server last reported it, or null while loading.</summary>
    private MediaDetail? Item { get; set; }

    /// <summary>Signed URLs for this item, refetched whenever its edits generation moves.</summary>
    private MediaLinks? Links { get; set; }

    /// <summary>What shows this file, or null while loading.</summary>
    private ReferenceImpact? Usage { get; set; }

    /// <summary>The metadata form's own state, so an unsaved edit is not mistaken for the item.</summary>
    private MetadataForm Metadata { get; } = new();

    /// <summary>The geometry form's own state.</summary>
    private GeometryForm Geometry { get; } = new();

    /// <summary>What the last write refused to do.</summary>
    private IReadOnlyList<ApiDiagnostic>? Errors { get; set; }

    /// <summary>What the last write reported without refusing.</summary>
    private string? Notice { get; set; }

    /// <summary>Whether a write is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>Whether this item is in the recycle bin.</summary>
    /// <remarks>
    /// Inferred from the fact that a deleted item is only reachable through the bin listing, which
    /// the API answers from the same endpoint. There is no column on the contract for it because a
    /// client has no business acting on one — restore is idempotent and purge is guarded.
    /// </remarks>
    private bool IsInBin { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync() => await LoadAsync();

    private async Task LoadAsync()
    {
        Item = await Client.GetAsync(Id);

        if (Item is null) return;

        Metadata.Read(Item);
        Geometry.Read(Item.Edits);

        // Refetched alongside the item rather than cached: every URL carries the item's edits
        // generation, so a set kept across a save would stop resolving the moment the editor
        // rotated anything (ADR 0007).
        Links = (await Client.LinksAsync([Id])).GetValueOrDefault(Id);
        Usage = await Client.WhereUsedAsync(Id);
    }

    /// <summary>The alternative text the preview carries; empty rather than null, never absent.</summary>
    private string PreviewAlt =>
        Item is null || Item.IsDecorative || string.IsNullOrWhiteSpace(Item.AltText)
            ? string.Empty
            : Item.AltText;

    private Task SaveMetadataAsync() => GuardedAsync(async () =>
    {
        var result = await Client.PatchAsync(Id, Metadata.ToRequest(Item!.RowVersion));

        if (!result.IsSuccess)
        {
            Errors = result.Errors;

            return;
        }

        Notice = "Saved.";

        await LoadAsync();
    });

    /// <remarks>
    /// Saving geometry bumps the item's edits generation, which changes every rendition URL the site
    /// emits for it — so every page showing this picture is corrected without any of them being
    /// republished, and browser and CDN copies of the old one are simply never asked for again
    /// (ADR 0007). That is the whole of acceptance criterion P5 #6, and it is why this button is
    /// worth a sentence of explanation beside it.
    /// </remarks>
    private Task SaveGeometryAsync() => GuardedAsync(async () =>
    {
        var result = await Client.SetEditsAsync(
            Id,
            new SetMediaEditsRequest(Geometry.ToEdits(), Item!.RowVersion));

        if (!result.IsSuccess)
        {
            Errors = result.Errors;

            return;
        }

        Notice = "Applied. Every page showing this picture now uses the new version.";

        await LoadAsync();
    });

    private Task RevertAsync() => GuardedAsync(async () =>
    {
        var result = await Client.RevertEditsAsync(Id);

        if (!result.IsSuccess)
        {
            Errors = result.Errors;

            return;
        }

        Notice = "Back to the file as it was uploaded.";

        await LoadAsync();
    });

    /// <remarks>
    /// Replacing keeps the id, so every page pointing at this item now shows the new file without
    /// any of them being edited — which is the point of the action and also the reason it is worth
    /// warning about beside the where-used list.
    /// </remarks>
    private Task ReplaceAsync(InputFileChangeEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        return GuardedAsync(async () =>
        {
            var file = args.File;

            await using var content = file.OpenReadStream(MaxUploadBytes);

            var result = await Client.ReplaceAsync(
                Id,
                new MediaUploadContent(content, file.Name, file.Size, Item!.FolderId));

            if (!result.IsSuccess)
            {
                Errors = result.Errors;

                return;
            }

            Notice = "Replaced. Every page showing this item now shows the new file.";

            await LoadAsync();
        });
    }

    private Task DeleteAsync() => GuardedAsync(async () =>
    {
        var result = await Client.DeleteAsync(Id);

        if (!result.IsSuccess)
        {
            Errors = result.Errors;

            return;
        }

        IsInBin = true;
        Notice = "Moved to the recycle bin. Pages still showing it will render nothing until it is restored.";
    });

    private Task RestoreAsync() => GuardedAsync(async () =>
    {
        var result = await Client.RestoreAsync(Id);

        if (!result.IsSuccess)
        {
            Errors = result.Errors;

            return;
        }

        IsInBin = false;
        Notice = "Restored.";

        await LoadAsync();
    });

    /// <remarks>
    /// The one irreversible action on this screen. The server refuses it while anything at all
    /// points at the item and answers with the reason; this screen shows that reason next to the
    /// list of what is pointing at it, which is the pair an editor needs to act (spec section 13.8).
    /// </remarks>
    private Task PurgeAsync() => GuardedAsync(async () =>
    {
        var result = await Client.PurgeAsync(Id);

        if (!result.IsSuccess)
        {
            Errors = result.Errors;

            return;
        }

        Navigation.NavigateTo("/admin/media");
    });

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
        }
    }

    /// <summary>
    /// The metadata form's own state.
    /// </summary>
    /// <remarks>
    /// Separate from the item so that an in-progress edit is never mistaken for what the server
    /// holds — and so that a save sends every field, which is what the <c>Patch</c> members on the
    /// request are for.
    /// </remarks>
    private sealed class MetadataForm
    {
        public string? AltText { get; set; }

        public bool IsDecorative { get; set; }

        public string? Title { get; set; }

        public string? Caption { get; set; }

        public string? Credit { get; set; }

        public void Read(MediaDetail item)
        {
            AltText = item.AltText;
            IsDecorative = item.IsDecorative;
            Title = item.Title;
            Caption = item.Caption;
            Credit = item.Credit;
        }

        public PatchMediaRequest ToRequest(string rowVersion) => new()
        {
            AltText = AltText,
            IsDecorative = IsDecorative,
            Title = Title,
            Caption = Caption,
            Credit = Credit,
            ExpectedRowVersion = rowVersion,
        };
    }

    /// <summary>
    /// The image editor's own state, in the normalized fractions the model stores.
    /// </summary>
    /// <remarks>
    /// Fractions rather than pixels, all the way to the control, because that is what is stored and
    /// what survives replacing the file with a higher-resolution original (spec section 13.4). A
    /// pixel-valued control would have to be recomputed every time the picture behind it changed
    /// size, and the first replacement would silently move every crop in the library.
    /// </remarks>
    private sealed class GeometryForm
    {
        public int Rotate { get; set; }

        public FlipDirection Flip { get; set; }

        public bool HasCrop { get; set; }

        public double CropX { get; set; }

        public double CropY { get; set; }

        public double CropWidth { get; set; } = 1;

        public double CropHeight { get; set; } = 1;

        public bool HasFocalPoint { get; set; }

        public double FocalX { get; set; } = 0.5;

        public double FocalY { get; set; } = 0.5;

        public void Read(MediaEdits? edits)
        {
            var current = edits ?? MediaEdits.None;

            Rotate = current.Rotate;
            Flip = current.Flip;
            HasCrop = current.Crop is not null;

            var crop = current.Crop ?? NormalizedRect.Full;

            CropX = crop.X;
            CropY = crop.Y;
            CropWidth = crop.Width;
            CropHeight = crop.Height;

            HasFocalPoint = current.FocalPoint is not null;

            var focal = current.FocalPoint ?? NormalizedPoint.Center;

            FocalX = focal.X;
            FocalY = focal.Y;
        }

        public MediaEdits ToEdits() => new(
            Rotate,
            Flip,
            HasCrop ? new NormalizedRect(CropX, CropY, CropWidth, CropHeight) : null,
            HasFocalPoint ? new NormalizedPoint(FocalX, FocalY) : null);
    }
}
