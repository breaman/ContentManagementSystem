using System.Text.Json;
using Microsoft.AspNetCore.Components;
using S2.DynamicSsr.Cms;

namespace S2.DynamicSsr.Content.Fields;

/// <summary>Renders a <c>reusable</c> value by resolving its published version asynchronously.</summary>
public partial class ReusableRenderer : CmsFieldRendererBase
{
    [Inject]
    private ReusableContentRepository Repository { get; set; } = default!;

    [Inject]
    private RenderDiagnostics Diagnostics { get; set; } = default!;

    [Inject]
    private ILogger<ReusableRenderer> Logger { get; set; } = default!;

    private string? Body { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        Body = null;

        if (!Value.TryGetProperty("reusableContentId", out var id) || id.ValueKind != JsonValueKind.Number)
        {
            return;
        }

        var reusableId = id.GetInt64();
        Context.CacheTags.Add($"ru:{reusableId}");

        var published = await Repository.GetPublishedAsync(reusableId);

        if (published is null)
        {
            // Spec §15.3: unpublished reusable content renders nothing and logs.
            Logger.LogWarning(
                "Reusable content {ReusableId} referenced by page {PageId} has no published version.",
                reusableId,
                Context.PageId);
            Diagnostics.Record($"reusable.unpublished id={reusableId} zone={ZoneKey} page={Context.PageId}");

            return;
        }

        Body = published;
    }
}
