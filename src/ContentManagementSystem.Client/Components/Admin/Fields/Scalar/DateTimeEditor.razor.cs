using System.Globalization;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>dateTime</c> editor — a point in time, stored with the offset it was chosen in
/// (spec section 7.1).
/// </summary>
/// <remarks>
/// <strong>An <c>&lt;input type="datetime-local"&gt;</c> has no time zone, and the field type
/// insists on one.</strong> That gap is this component's entire job: what the browser hands over is
/// wall-clock text, and it is completed with the offset the browser is actually running at before it
/// is stored. Storing <c>2026-08-12T09:30:00</c> as written would name no instant at all — one thing
/// to the browser that submitted it, another to the server that holds it, and a third to the
/// scheduler that acts on it.
/// <para>
/// The offset comes from <see cref="TimeProvider"/> rather than from <see cref="DateTimeOffset.Now"/>
/// so a test can state what zone the browser is in. In the browser it is the machine's, which is the
/// zone the author is reading the clock in.
/// </para>
/// <para>
/// Both directions round-trip: a stored instant is converted to the author's local time to fill the
/// control, so an embargo set by a colleague in another office reads as the time it will actually
/// happen here rather than as the time it was typed there.
/// </para>
/// </remarks>
public partial class DateTimeEditor : FieldEditorBase
{
    /// <summary>The shape an <c>&lt;input type="datetime-local"&gt;</c> reads and writes.</summary>
    private const string LocalFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>The clock, so the offset an instant is completed with is stateable in a test.</summary>
    [Inject]
    private TimeProvider Clock { get; set; } = default!;

    /// <summary>The stored instant, verbatim.</summary>
    private string? Stored => StoredValue.ReadText(Value);

    /// <summary>The stored instant as local wall-clock text, or empty when nothing is authored.</summary>
    private string Local =>
        Stored is { Length: > 0 } stored &&
        DateTimeOffset.TryParse(
            stored,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var instant)
            ? TimeZoneInfo
                .ConvertTime(instant, Clock.LocalTimeZone)
                .ToString(LocalFormat, CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>What to call the zone the control is being read in.</summary>
    private string ZoneName => Clock.LocalTimeZone.StandardName;

    private string ZoneId => $"{Field.ControlId}-zone";

    private string DescribedBy => string.Join(
        ' ',
        new[] { Field.DescribedBy, ZoneId }.Where(id => !string.IsNullOrEmpty(id)));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>Completes what was typed with the browser's offset and stores it.</summary>
    private Task OnChangedAsync(ChangeEventArgs args)
    {
        var typed = args.Value?.ToString();

        if (string.IsNullOrWhiteSpace(typed) ||
            !DateTime.TryParse(
                typed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var wallClock))
        {
            return WriteAsync(string.Empty);
        }

        // The offset for that wall-clock reading rather than for now, so a time chosen on the far
        // side of a daylight-saving change is stored as the instant it will actually be.
        var offset = Clock.LocalTimeZone.GetUtcOffset(wallClock);
        var instant = new DateTimeOffset(wallClock, offset);

        return WriteAsync(StoredValue.Write(
            Value,
            FieldTypeKey,
            JsonValue.Create(instant.ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture))));
    }
}
