using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering;

/// <summary>Which level of the render tree a boundary is guarding.</summary>
/// <remarks>
/// An enum rather than the free string the S2 spike used: the value ends up in a log message an
/// operator filters on, and two spellings of "block" make that filter quietly incomplete.
/// </remarks>
public enum CmsRenderScope
{
    /// <summary>A whole zone of the page.</summary>
    Zone = 0,

    /// <summary>One block instance inside a block list.</summary>
    Block = 1,
}

/// <summary>
/// Isolates a failing renderer so one broken zone or block cannot take the page down
/// (spec section 15.3, task P3-11).
/// </summary>
/// <remarks>
/// Derived from <see cref="ErrorBoundaryBase"/> rather than from the stock <c>ErrorBoundary</c>, on
/// the S2 spike's recommendation, for two reasons that both matter on a public page. The stock one
/// renders the words "An error has occurred." into the document, which is not acceptable output for
/// an anonymous visitor; and it logs through <c>IErrorBoundaryLogger</c>, which knows nothing about
/// pages — overriding <see cref="OnErrorAsync"/> is what gets the page id, zone key, version id, and
/// block id into the log line, and that line is acceptance criterion <c>P3 #8</c>.
/// <para>
/// <strong>Boundaries sit at two levels, per zone and per block.</strong> A zone-level boundary alone
/// would let one failing block blank the whole zone around it, which for a body zone is most of the
/// page. The block-level boundary is the one that does the real work; the zone-level one catches
/// whatever is left — a field renderer that is not a block list, or the block list renderer itself.
/// </para>
/// <para>
/// Caught, logged, and rendered as the supplied error content, which is deliberately a marker
/// element and never a message: an editor looking at the page source can see which zone or block
/// failed, and a reader sees the rest of the page. Markers are elements or attributes rather than
/// HTML comments, because the Razor compiler strips comments out of <c>.razor</c> markup before they
/// ever reach a response.
/// </para>
/// </remarks>
public partial class CmsErrorBoundary : ErrorBoundaryBase
{
    /// <summary>The zone key the failing content sits under.</summary>
    [Parameter]
    public string ZoneKey { get; set; } = string.Empty;

    /// <summary>Which level this boundary guards.</summary>
    [Parameter]
    public CmsRenderScope Scope { get; set; } = CmsRenderScope.Zone;

    /// <summary>The failing block's id, when this boundary guards one block.</summary>
    [Parameter]
    public Guid? BlockId { get; set; }

    /// <summary>The block type key of the failing block, for the log line.</summary>
    [Parameter]
    public string? BlockTypeKey { get; set; }

    /// <summary>The render context, cascaded by the delivery host.</summary>
    /// <remarks>
    /// Nullable, unlike on the components it guards. A boundary whose own logging threw a null
    /// reference while reporting somebody else's failure would turn an isolated fault into the
    /// unhandled exception this component exists to prevent.
    /// </remarks>
    [CascadingParameter]
    public RenderContext? Context { get; set; }

    [Inject]
    private ILogger<CmsErrorBoundary> Logger { get; set; } = default!;

    /// <summary>The scope as the marker attribute spells it.</summary>
    /// <remarks>
    /// Written out rather than lower-cased from the enum name. The marker is part of the rendered
    /// page and something will eventually select on it, so it must not be derived through a
    /// culture-sensitive casing of a CLR identifier.
    /// </remarks>
    private string ScopeMarker => Scope switch
    {
        CmsRenderScope.Block => "block",
        _ => "zone",
    };

    /// <summary>Logs the failure with everything needed to find the content that caused it.</summary>
    /// <param name="exception">What the renderer threw.</param>
    /// <returns>A completed task; nothing here awaits anything.</returns>
    /// <remarks>
    /// The four facts in this line are the acceptance criterion, not decoration. A stack trace names
    /// a component; it does not name which of the four hundred pages built on that component was
    /// being rendered, nor which zone of it, nor which version — and without those an operator
    /// cannot reproduce the failure or tell an editor what to fix.
    /// </remarks>
    protected override Task OnErrorAsync(Exception exception)
    {
        Logger.LogError(
            exception,
            "CMS render failure in {Scope} '{ZoneKey}' (block {BlockId} '{BlockTypeKey}') on page " +
            "{PageId}, version {VersionId}. The rest of the page still renders.",
            Scope,
            ZoneKey,
            BlockId,
            BlockTypeKey,
            Context?.Page.Id,
            Context?.Page.VersionId);

        return Task.CompletedTask;
    }
}
