using System.Globalization;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Server.Delivery.Preview;

/// <summary>
/// The floating preview toolbar (task P3-16, spec section 12.1).
/// </summary>
/// <remarks>
/// It says three things, and the reason for each is the same: preview is where somebody decides
/// whether to publish, and every one of those decisions has been made about the wrong version by
/// somebody at some point. The <em>version number</em> distinguishes the draft from the version
/// already live; the <em>status</em> says which of those is on screen in a word; the <em>exit</em>
/// puts the editor back where they came from rather than leaving them on a page that looks live but
/// is not.
/// <para>
/// It lives in the outer chrome document and never inside the page, so the page in the frame is what
/// the public would get. A toolbar injected into the rendered markup would be the one difference
/// between preview and delivery, sitting in the middle of the thing preview exists to verify.
/// </para>
/// </remarks>
public partial class PreviewToolbar : ComponentBase
{
    /// <summary>What is on screen, and where the links go.</summary>
    [Parameter]
    [EditorRequired]
    public PreviewChrome Chrome { get; set; } = default!;

    /// <summary>
    /// The status word, preferring the live/draft facts over the raw lifecycle name.
    /// </summary>
    /// <remarks>
    /// "Published" is what an editor needs to see about a version that is currently being served,
    /// and the lifecycle status of that row can legitimately be <c>Archived</c> in states the
    /// publishing service passes through. The question being answered is "is this what the public
    /// sees", so it is answered from the page's own pointers rather than from the enum.
    /// </remarks>
    protected string StatusLabel => Chrome.Version switch
    {
        { IsPublished: true } => "Published",
        { IsDraft: true } => "Draft",
        var version => version.Status,
    };

    /// <summary>The badge colouring, so live and not-live are distinguishable at a glance.</summary>
    protected string StatusCssClass =>
        $"cms-preview-toolbar__status cms-preview-toolbar__status--{StatusLabel.ToLowerInvariant()}";

    /// <summary>
    /// How long a shared link has left, or null for an editor's own preview.
    /// </summary>
    /// <remarks>
    /// A date rather than a countdown, formatted under <see cref="CultureInfo.InvariantCulture"/> in
    /// UTC and saying so. The page is served to whoever holds the link, from wherever they are, and
    /// a time rendered in the server's culture and offset is a time that is wrong for most of them.
    /// </remarks>
    protected string? Expiry => Chrome.ExpiresOn is { } expires
        ? $"Link expires {expires.UtcDateTime.ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture)} UTC"
        : null;

    /// <summary>The classes of one device button, marking the active one.</summary>
    /// <param name="device">The device the button selects.</param>
    protected string DeviceCssClass(PreviewDevice device) =>
        device == Chrome.Device
            ? "cms-preview-toolbar__device cms-preview-toolbar__device--active"
            : "cms-preview-toolbar__device";
}
