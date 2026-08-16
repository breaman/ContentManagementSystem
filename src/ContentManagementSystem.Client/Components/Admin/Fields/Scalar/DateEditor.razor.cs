using System.Globalization;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Scalar;

/// <summary>
/// The <c>date</c> editor — a calendar day with no time of day (spec section 7.1).
/// </summary>
/// <remarks>
/// Nothing is converted here, in either direction, and that is the whole implementation. The field
/// type stores ISO-8601 <c>YYYY-MM-DD</c>, which is exactly what an <c>&lt;input type="date"&gt;</c>
/// reads and writes, so the stored text goes straight into the control and back out again.
/// <para>
/// Passing it through a <see cref="DateTime"/> would be the bug: "the 12th" means the 12th wherever
/// it is read, and giving it a time and an offset on the way through moves it across a date boundary
/// for a browser in another time zone. That is precisely how a "published on" date ends up a day
/// out, and the field type refuses to store an instant for the same reason.
/// </para>
/// </remarks>
public partial class DateEditor : FieldEditorBase
{
    /// <summary>The stored date, verbatim.</summary>
    private string Stored => StoredValue.ReadText(Value) ?? string.Empty;

    /// <summary>The earliest date the slot allows.</summary>
    private string? Min => IsoDate(ConfiguredText(FieldSettingNames.Min));

    /// <summary>The latest date the slot allows.</summary>
    private string? Max => IsoDate(ConfiguredText(FieldSettingNames.Max));

    private string InvalidClass => Field.Severity is ZoneSeverity.Error ? "is-invalid" : string.Empty;

    /// <summary>
    /// A configured bound, dropped unless it is a date the control can honour.
    /// </summary>
    /// <remarks>
    /// A bound the browser cannot parse is worse than no bound: Chrome disables the whole picker for
    /// a malformed <c>min</c>, so a typo in a template's configuration would leave an author unable
    /// to choose any date at all rather than merely unbounded. The publish check still enforces the
    /// real rule.
    /// </remarks>
    private static string? IsoDate(string? configured) =>
        configured is { Length: > 0 } text &&
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? text
            : null;

    private Task OnChangedAsync(ChangeEventArgs args)
    {
        var typed = args.Value?.ToString();

        return string.IsNullOrWhiteSpace(typed)
            ? WriteAsync(string.Empty)
            : WriteAsync(StoredValue.Write(Value, FieldTypeKey, JsonValue.Create(typed)));
    }
}
