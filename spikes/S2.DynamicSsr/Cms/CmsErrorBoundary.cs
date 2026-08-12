using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace S2.DynamicSsr.Cms;

/// <summary>
/// Per-zone and per-block error boundary (spec §15.3). Derives from <see cref="ErrorBoundaryBase"/>
/// rather than <c>ErrorBoundary</c> so the failure is logged with the page id, zone key, and version
/// id, and so the fallback markup is ours rather than "An error has occurred."
/// </summary>
public sealed class CmsErrorBoundary : ErrorBoundaryBase
{
    [Parameter]
    public string ZoneKey { get; set; } = string.Empty;

    [Parameter]
    public string Kind { get; set; } = "zone";

    [Parameter]
    public string? BlockId { get; set; }

    [CascadingParameter]
    public CmsRenderContext Context { get; set; } = default!;

    [Inject]
    public RenderDiagnostics Diagnostics { get; set; } = default!;

    [Inject]
    public ILogger<CmsErrorBoundary> Logger { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (CurrentException is null)
        {
            builder.AddContent(0, ChildContent);
        }
        else if (ErrorContent is not null)
        {
            builder.AddContent(1, ErrorContent(CurrentException));
        }

        // No ErrorContent and a caught exception renders nothing at all: the failing block
        // disappears and the rest of the page survives.
    }

    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(
            exception,
            "CMS render failure in {Kind} '{ZoneKey}'{BlockSuffix} on page {PageId}, version {VersionId}.",
            Kind,
            ZoneKey,
            BlockId is null ? string.Empty : $" (block {BlockId})",
            Context.PageId,
            Context.VersionId);

        Diagnostics.Record(
            $"render.failure kind={Kind} zone={ZoneKey} block={BlockId ?? "-"} " +
            $"page={Context.PageId} version={Context.VersionId} exception={exception.GetType().Name}");

        return Task.CompletedTask;
    }
}
