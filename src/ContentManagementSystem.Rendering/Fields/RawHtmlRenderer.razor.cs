using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders an <c>html</c> value — hand-written markup, embeds, third-party widgets
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// Named for the field type's key rather than after the type, because
/// <c>Microsoft.AspNetCore.Components.Web.HtmlRenderer</c> already owns the obvious name and a
/// collision inside a <c>.razor</c> file resolves to whichever <c>@using</c> came last.
/// <para>
/// Sanitized again on the way out under <see cref="SanitizationProfile.Developer"/>, matching what
/// the field type applied on the way in. The role that lets someone author this widens the
/// allowlist; it does not remove it, and a render-time pass is what covers rows that reached the
/// database without passing the write-time one (ADR 0008).
/// </para>
/// <para>
/// The profile is fixed rather than configurable. The <c>html</c> field type declares only
/// <c>maxLength</c>, and inventing a setting here would be a permission the configuration schema
/// refuses to store — the value would be silently ignored, which is precisely the failure a closed
/// schema exists to prevent.
/// </para>
/// </remarks>
public partial class RawHtmlRenderer : CmsFieldRendererBase
{
    [Inject]
    private IContentSanitizer Sanitizer { get; set; } = default!;

    /// <summary>The sanitized markup to emit; empty when there is nothing stored.</summary>
    protected MarkupString Html { get; private set; }

    /// <inheritdoc />
    protected override void OnParametersSet() =>
        Html = ValueText is { Length: > 0 } markup
            ? new MarkupString(Sanitizer.Sanitize(markup, SanitizationProfile.Developer))
            : default;
}
