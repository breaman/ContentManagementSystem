using System.Globalization;

using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Workflow;

/// <summary>
/// When a page publishes and when it stops being served (task P7-16, spec section 11.6).
/// </summary>
/// <remarks>
/// <strong>The offset is shown, always.</strong> An editor picks a wall-clock time; a wall-clock
/// time is ambiguous twice a year and wrong once, and "publish at 9am" during a DST transition is a
/// real support ticket. So the box takes a local time and the line beneath it states the exact
/// instant that produces, offset included, before anything is saved.
/// <para>
/// The instant is computed in the site's time zone rather than the browser's. An editor in another
/// country scheduling a press release means nine o'clock <em>for the site</em>, and a browser-local
/// reading would publish it at the wrong hour without anybody being able to see why.
/// </para>
/// </remarks>
public partial class SchedulePanel : ComponentBase
{
    /// <summary>The <c>datetime-local</c> input's format, which has no offset and no seconds.</summary>
    private const string LocalFormat = "yyyy-MM-ddTHH:mm";

    /// <summary>Reads and writes the schedule.</summary>
    [Inject]
    private IWorkflowClient Client { get; set; } = default!;

    /// <summary>The page being scheduled.</summary>
    [Parameter]
    [EditorRequired]
    public int PageId { get; set; }

    /// <summary>The schedule, or null while loading or when the caller may not see it.</summary>
    [PersistentState]
    public PageScheduleState? State { get; set; }

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    /// <summary>What went wrong with the last save, if anything.</summary>
    private string? Error { get; set; }

    /// <summary>Whether anything is scheduled at all.</summary>
    private bool HasSchedule => State is { PublishOn: not null } or { UnpublishOn: not null };

    /// <summary>The exact instant the publish box currently means.</summary>
    private string PublishHelp => Describe(_publishLocal, "It will not publish on a schedule.");

    /// <summary>The exact instant the retirement box currently means.</summary>
    private string UnpublishHelp => Describe(_unpublishLocal, "It will stay published until somebody retires it.");

    private string? _publishLocal;
    private string? _unpublishLocal;
    private TimeZoneInfo _zone = TimeZoneInfo.Utc;

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (State?.PageId == PageId) return;

        State = await Client.GetScheduleAsync(PageId);
        Apply(State);
    }

    private void OnPublishChanged(ChangeEventArgs args) => _publishLocal = args.Value?.ToString();

    private void OnUnpublishChanged(ChangeEventArgs args) => _unpublishLocal = args.Value?.ToString();

    private Task SaveAsync() => SendAsync(new SetScheduleRequest(
        ToInstant(_publishLocal),
        ToInstant(_unpublishLocal)));

    private Task ClearAsync() => SendAsync(new SetScheduleRequest(null, null));

    private async Task SendAsync(SetScheduleRequest request)
    {
        IsBusy = true;
        Error = null;

        try
        {
            var result = await Client.SetScheduleAsync(PageId, request);

            if (result is null)
            {
                Error = "That schedule could not be saved. A scheduled time has to be in the future, " +
                    "and the retirement has to come after the publish.";

                return;
            }

            State = result;
            Apply(result);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Fills the two boxes from a loaded schedule, in the site's time zone.</summary>
    private void Apply(PageScheduleState? state)
    {
        _zone = Resolve(state?.TimeZoneId);
        _publishLocal = ToLocal(state?.PublishOn);
        _unpublishLocal = ToLocal(state?.UnpublishOn);
    }

    /// <summary>
    /// Turns a wall-clock string into the instant it names in the site's time zone.
    /// </summary>
    /// <remarks>
    /// The two awkward cases are handled explicitly rather than left to whichever overload happened
    /// to be called. A time that does not exist — the hour skipped when the clocks go forward —
    /// takes the offset in force before the transition, so it lands at the first real moment after
    /// it rather than being refused. A time that happens twice takes the earlier of the two, which
    /// is what an editor means by "at two o'clock" and is also the safer of the two for a publish.
    /// </remarks>
    private DateTimeOffset? ToInstant(string? local)
    {
        if (string.IsNullOrWhiteSpace(local)) return null;

        if (!DateTime.TryParse(
                local,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var wallClock))
        {
            return null;
        }

        var unspecified = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        var offsets = _zone.GetAmbiguousTimeOffsets(unspecified);

        var offset = _zone.IsAmbiguousTime(unspecified) && offsets.Length > 0
            ? offsets.Max()
            : _zone.GetUtcOffset(unspecified);

        return new DateTimeOffset(unspecified, offset);
    }

    /// <summary>Turns a stored instant into the wall-clock string the box shows.</summary>
    private string? ToLocal(DateTimeOffset? instant) =>
        instant is { } value
            ? TimeZoneInfo.ConvertTime(value, _zone).ToString(LocalFormat, CultureInfo.InvariantCulture)
            : null;

    /// <summary>States the exact instant a box currently means, offset and all.</summary>
    private string Describe(string? local, string whenEmpty) =>
        ToInstant(local) is { } instant
            ? $"That is {instant:dddd d MMMM yyyy HH:mm} (UTC{instant.Offset.Hours:+00;-00}:{Math.Abs(instant.Offset.Minutes):00})."
            : whenEmpty;

    /// <summary>
    /// Finds the site's time zone, falling back to UTC rather than throwing.
    /// </summary>
    /// <remarks>
    /// WebAssembly carries a time zone database, but a site configured with a Windows zone id
    /// running on a Linux host — or the reverse — can still name one this runtime does not know.
    /// Falling back to UTC and showing the offset is wrong in a way an editor can see; throwing here
    /// would take the whole panel out.
    /// </remarks>
    private static TimeZoneInfo Resolve(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException
            or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
