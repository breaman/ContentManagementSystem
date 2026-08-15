using System.Globalization;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Rendering.Fields;

/// <summary>
/// Renders a <c>date</c> value — a calendar date with no time and no offset (spec section 7.1).
/// </summary>
/// <remarks>
/// Formatted under <see cref="CultureInfo.InvariantCulture"/>, whose month names are English, so the
/// page reads the same whatever culture the server process happens to be running under. That is a
/// correctness property rather than a stylistic one: a rendered page is cached and served to
/// everybody, so it cannot depend on ambient state that differs between two machines behind the same
/// load balancer.
/// <para>
/// No time zone conversion is applied, and applying one would be the bug. "The 12th" means the 12th
/// wherever it is read; giving it an instant and shifting it moves a "published on" date a day out
/// for half the world's readers.
/// </para>
/// </remarks>
public partial class DateRenderer : CmsFieldRendererBase
{
    /// <summary>The one form the field type stores, and therefore the only one parsed here.</summary>
    private const string StoredFormat = "yyyy-MM-dd";

    [Inject]
    private ILogger<DateRenderer> Logger { get; set; } = default!;

    /// <summary>The stored ISO text, emitted as the machine-readable value.</summary>
    protected string Iso { get; private set; } = string.Empty;

    /// <summary>The human-readable date; empty when there is nothing renderable.</summary>
    protected string Display { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        Iso = string.Empty;
        Display = string.Empty;

        if (ValueText is not { Length: > 0 } text) return;

        if (!DateOnly.TryParseExact(text, StoredFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var date))
        {
            // Unreachable through the save path — the field type refuses anything but this form —
            // so a value here means an import or a restore wrote it. Rendered as nothing rather
            // than as its raw text, because a half-parsed date shown to a reader is worse than a
            // gap an editor can see and fix.
            Logger.LogWarning(
                "Date in '{PropertyKey}' on page {PageId} version {VersionId} is not stored as " +
                "{Format}, so it renders nothing.",
                PropertyKey,
                Context?.Page.Id,
                Context?.Page.VersionId,
                StoredFormat);

            return;
        }

        Iso = text;
        Display = date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
    }
}
