using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>dateTime</c> value — an instant, stored with an explicit offset
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// Displayed in UTC, labelled as UTC. The alternative would be the site's configured time zone,
/// which <c>SiteSettings.TimeZoneId</c> already carries; that is deliberately not read here, because
/// a rendered page is cached and served to every visitor, so it must not be built from anything that
/// varies by reader — and a time shown without saying which zone it is in is the one presentation
/// that is actively wrong.
/// <para>
/// The offset the author stored still reaches the browser: the <c>datetime</c> attribute is the
/// stored text verbatim, so a script or a feed reader gets the instant as authored rather than a
/// re-serialization of it.
/// </para>
/// </remarks>
public partial class DateTimeRenderer : CmsFieldRendererBase
{
    [Inject]
    private ILogger<DateTimeRenderer> Logger { get; set; } = default!;

    /// <summary>The stored ISO instant, emitted as the machine-readable value.</summary>
    protected string Iso { get; private set; } = string.Empty;

    /// <summary>The human-readable instant; empty when there is nothing renderable.</summary>
    protected string Display { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Iso = string.Empty;
        Display = string.Empty;

        if (ValueText is not { Length: > 0 } text) return;

        // RoundtripKind so a stored 'Z' is read as the offset it is rather than as local time on
        // whatever machine is rendering — the failure that moves an instant by the server's offset.
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind,
                out var instant))
        {
            Logger.LogWarning(
                "Date and time in '{PropertyKey}' on page {PageId} version {VersionId} is not a " +
                "readable instant, so it renders nothing.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId);

            return;
        }

        Iso = text;
        Display = instant.ToUniversalTime()
            .ToString("MMMM d, yyyy 'at' h:mm tt 'UTC'", CultureInfo.InvariantCulture);
    }
}
