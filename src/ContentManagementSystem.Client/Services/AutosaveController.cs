namespace ContentManagementSystem.Client.Services;

/// <summary>What one autosave attempt did.</summary>
public enum AutosaveOutcome
{
    /// <summary>The draft was written.</summary>
    Saved = 0,

    /// <summary>
    /// The attempt failed for a reason that may not recur — the network, or a server that was busy.
    /// </summary>
    Transient = 1,

    /// <summary>
    /// The server understood the request and refused it: invalid content, or a save conflict.
    /// </summary>
    /// <remarks>
    /// Retrying this is pointless and worse than pointless. The same refusal every twenty seconds
    /// buries the one message that would let the editor fix it, and a conflict in particular needs a
    /// decision from a person before anything is sent again (task P6-19).
    /// </remarks>
    Refused = 2,
}

/// <summary>The outcome of one attempt, and anything worth saying about it.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Message">Why, when the indicator should say so.</param>
public sealed record AutosaveResult(AutosaveOutcome Outcome, string? Message = null)
{
    /// <summary>The ordinary case.</summary>
    public static AutosaveResult Saved { get; } = new(AutosaveOutcome.Saved);
}

/// <summary>
/// Saves a draft twenty seconds after the typing stops, and keeps trying when it cannot
/// (task P6-18, spec section 11.3).
/// </summary>
/// <remarks>
/// One of these belongs to one open editor. It owns three things and deliberately nothing else: when
/// a save is due, whether one is in flight, and what the indicator should say — <em>what</em> gets
/// saved is the screen's, handed in as a delegate, so the same debounce serves a page, a reusable
/// item, and whatever comes next.
/// <para>
/// <strong>Nothing typed is ever dropped.</strong> There is no queue of payloads because a queue of
/// payloads would be a queue of stale ones: the delegate reads the editor's current state at the
/// moment it runs, so a failed attempt followed by more typing saves the later text, once. What the
/// controller queues is the <em>intent</em> to save, and it holds that until a save succeeds — over
/// a failure, over a retry, and over the editor going offline and coming back
/// (acceptance criterion P6 #5).
/// </para>
/// <para>
/// The clock is a <see cref="TimeProvider"/> so the twenty seconds can be advanced in a test rather
/// than waited out. A suite that really slept would take longer than the feature it tests.
/// </para>
/// </remarks>
public sealed class AutosaveController : IAsyncDisposable
{
    /// <summary>
    /// How long the typing has to stop before a save is due (spec section 11.3).
    /// </summary>
    public static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait before the first retry; each later one waits twice as long.</summary>
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>The longest a retry ever waits, so a long outage still recovers promptly.</summary>
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock;

    private readonly Func<CancellationToken, Task<AutosaveResult>> _save;

    private readonly Func<Func<Task>, Task> _dispatch;

    private readonly ITimer _timer;

    private readonly CancellationTokenSource _closed = new();

    /// <summary>Whether there is work the editor has done that the server has not seen.</summary>
    private bool _dirty;

    /// <summary>The attempt in flight, so a flush waits for it rather than racing it.</summary>
    private Task _inFlight = Task.CompletedTask;

    /// <summary>How many attempts the current save has taken.</summary>
    private int _attempt;

    /// <summary>
    /// Creates a controller for one open editor.
    /// </summary>
    /// <param name="clock">The clock the debounce and the retries are measured on.</param>
    /// <param name="save">
    /// Writes whatever the editor currently holds. Called with a token that is cancelled when the
    /// editor closes, and expected to answer rather than throw — an exception is treated as
    /// transient, since the alternative is an unobserved task and an editor told nothing.
    /// </param>
    /// <param name="dispatch">
    /// Runs work on the renderer's synchronization context, in practice a component's
    /// <c>InvokeAsync</c>. A timer callback arrives on a thread pool thread, and a save that touched
    /// component state from there would be a race with the renderer.
    /// </param>
    public AutosaveController(
        TimeProvider clock,
        Func<CancellationToken, Task<AutosaveResult>> save,
        Func<Func<Task>, Task>? dispatch = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(save);

        _clock = clock;
        _save = save;
        _dispatch = dispatch ?? (action => action());

        // Created stopped. A screen that opens and is never typed into must not write to the draft:
        // it would move the page's modified date and make "has unpublished changes" true for a
        // person who only looked.
        _timer = clock.CreateTimer(_ => OnDue(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Raised whenever <see cref="Status"/> changes, so a screen can redraw.</summary>
    public event Action? Changed;

    /// <summary>Where the editor's work has got to.</summary>
    public AutosaveStatus Status { get; private set; } = AutosaveStatus.Clean;

    /// <summary>The attempt in flight, for a caller that has to know it finished.</summary>
    public Task Pending => _inFlight;

    /// <summary>
    /// Records that the editor changed something, and restarts the idle countdown.
    /// </summary>
    /// <remarks>
    /// Restarts rather than extends: twenty seconds of <em>inactivity</em> is the rule, so somebody
    /// writing a long paragraph is not interrupted mid-sentence by a save that fires on a fixed
    /// schedule regardless of what they are doing.
    /// </remarks>
    public void Touch()
    {
        if (_closed.IsCancellationRequested) return;

        _dirty = true;
        _attempt = 0;

        // A refusal is retired by the next edit, exactly as a stale validation badge is: it was
        // about content that no longer exists, and leaving it up would report an old problem
        // against new text.
        Update(Status with { Phase = AutosavePhase.Pending, Attempt = 0, Message = null });

        _timer.Change(IdleDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Saves now, if there is anything to save, and waits for the answer.
    /// </summary>
    /// <remarks>
    /// What navigating away calls, and what the explicit save button calls. It waits for an attempt
    /// already in flight rather than starting a second one — two concurrent writes of the same draft
    /// would have the second lose to the first on the row version, turning an editor's own save into
    /// a conflict with themselves.
    /// </remarks>
    public async Task FlushAsync()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        await _inFlight;

        if (!_dirty || _closed.IsCancellationRequested) return;

        await (_inFlight = SaveAsync());
    }

    /// <summary>
    /// Declares the editor's work saved by something other than this controller.
    /// </summary>
    /// <param name="savedOn">When it was saved, or null to leave the reported time alone.</param>
    /// <remarks>
    /// A publish, a discard, or a reload all leave the server holding what the screen holds. Without
    /// this the indicator would keep claiming unsaved work and the next navigation would write a
    /// draft nobody changed.
    /// </remarks>
    public void MarkSaved(DateTimeOffset? savedOn = null)
    {
        _dirty = false;
        _attempt = 0;

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        Update(new AutosaveStatus(AutosavePhase.Saved, savedOn ?? Status.SavedOn));
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _closed.CancelAsync();

        _timer.Dispose();
        _closed.Dispose();

        // Whatever was in flight is left to finish or fail on its own. Waiting here would block a
        // component's disposal on a request that may be the very one the network is refusing.
        GC.SuppressFinalize(this);
    }

    /// <summary>The idle countdown ran out.</summary>
    private void OnDue() => _ = _dispatch(async () =>
    {
        if (!_dirty || _closed.IsCancellationRequested) return;

        await _inFlight;

        if (!_dirty || _closed.IsCancellationRequested) return;

        await (_inFlight = SaveAsync());
    });

    /// <summary>Makes one attempt and decides what happens next.</summary>
    private async Task SaveAsync()
    {
        _attempt++;

        Update(Status with { Phase = AutosavePhase.Saving, Attempt = _attempt, Message = null });

        AutosaveResult result;

        try
        {
            result = await _save(_closed.Token);
        }
        catch (OperationCanceledException) when (_closed.IsCancellationRequested)
        {
            // The editor closed the screen mid-request. There is nobody left to tell.
            return;
        }
        catch (Exception exception)
        {
            // Every remaining exception is treated as transient, including the ones that are not.
            // A dropped connection and a timeout — the two ordinary ones — genuinely are; and for
            // anything else, an autosave that stopped for good on an exception nobody caught would
            // leave the editor typing into a screen that has quietly stopped saving, which is worse
            // than one wasted retry. What it must never do is disappear silently.
            result = new AutosaveResult(AutosaveOutcome.Transient, exception.Message);
        }

        switch (result.Outcome)
        {
            case AutosaveOutcome.Saved:
                _dirty = false;
                _attempt = 0;

                Update(new AutosaveStatus(AutosavePhase.Saved, _clock.GetUtcNow()));

                break;

            case AutosaveOutcome.Transient:
                // Still dirty, deliberately. The edit is held and the attempt is made again; what
                // must never happen is the controller deciding on the editor's behalf that a piece
                // of writing was not worth keeping.
                Update(Status with
                {
                    Phase = AutosavePhase.Retrying,
                    Attempt = _attempt,
                    Message = result.Message,
                });

                _timer.Change(RetryDelay(_attempt), Timeout.InfiniteTimeSpan);

                break;

            default:
                Update(Status with
                {
                    Phase = AutosavePhase.Refused,
                    Attempt = _attempt,
                    Message = result.Message,
                });

                break;
        }
    }

    /// <summary>How long to wait before attempt <paramref name="attempt"/> plus one.</summary>
    /// <remarks>
    /// Doubling, capped. An outage that lasts an hour should not end with the editor waiting an hour
    /// after it clears, and a server that is briefly overloaded should not be hammered by every open
    /// editor in the building at the same interval.
    /// </remarks>
    private static TimeSpan RetryDelay(int attempt)
    {
        var seconds = FirstRetryDelay.TotalSeconds * Math.Pow(2, Math.Min(attempt - 1, 10));

        return TimeSpan.FromSeconds(Math.Min(seconds, MaximumRetryDelay.TotalSeconds));
    }

    /// <summary>Publishes a new status, and only when it actually differs.</summary>
    private void Update(AutosaveStatus status)
    {
        if (status == Status) return;

        Status = status;

        Changed?.Invoke();
    }
}
