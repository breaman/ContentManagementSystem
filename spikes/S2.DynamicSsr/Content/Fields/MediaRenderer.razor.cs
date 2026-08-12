using System.Text.Json;
using Microsoft.AspNetCore.Components;
using S2.DynamicSsr.Cms;

namespace S2.DynamicSsr.Content.Fields;

/// <summary>
/// Renders a <c>media</c> value, and is where a cache tag enters the render context: the tag set is
/// derived from what actually rendered rather than from a hand-maintained list (spec §15.2).
/// </summary>
public partial class MediaRenderer : CmsFieldRendererBase
{
    [Inject]
    private MediaRepository Media { get; set; } = default!;

    [Inject]
    private RenderDiagnostics Diagnostics { get; set; } = default!;

    [Inject]
    private ILogger<MediaRenderer> Logger { get; set; } = default!;

    private MediaItem? Item { get; set; }

    private long? MissingId { get; set; }

    private string AltOverride { get; set; } = string.Empty;

    protected override void OnParametersSet()
    {
        Item = null;
        MissingId = null;

        if (!Value.TryGetProperty("mediaId", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var mediaId = id.GetInt64();

        Context.CacheTags.Add($"media:{mediaId}");

        AltOverride = Value.TryGetProperty("altOverride", out var alt) && alt.ValueKind == JsonValueKind.String
            ? alt.GetString()!
            : string.Empty;

        if (Media.TryGet(mediaId, out var item))
        {
            Item = AltOverride.Length == 0 ? item : item with { AltText = AltOverride };

            return;
        }

        MissingId = mediaId;
        Logger.LogWarning("Media {MediaId} referenced by page {PageId} is missing.", mediaId, Context.PageId);
        Diagnostics.Record($"media.missing id={mediaId} zone={ZoneKey} page={Context.PageId}");
    }
}
