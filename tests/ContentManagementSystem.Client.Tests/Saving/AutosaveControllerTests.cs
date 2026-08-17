using ContentManagementSystem.Client.Services;

using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Saving;

/// <summary>
/// Autosave's debounce, its retries, and its promise (task P6-18, acceptance criterion P6 #5).
/// </summary>
/// <remarks>
/// The promise is the part worth testing: an editor's typing is never dropped, whatever the network
/// does. These drive the clock rather than waiting on it — a suite that really slept twenty seconds
/// per case would take longer to run than the feature takes to use.
/// </remarks>
public class AutosaveControllerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task NothingIsSavedUntilTheTypingHasStoppedForTwentySeconds()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(AutosaveResult.Saved);
        });

        autosave.Touch();

        clock.Advance(TimeSpan.FromSeconds(19));
        attempts.Should().Be(0, "the rule is twenty seconds of inactivity, not nineteen");

        clock.Advance(TimeSpan.FromSeconds(1));
        await autosave.Pending;

        attempts.Should().Be(1);
        autosave.Status.Phase.Should().Be(AutosavePhase.Saved);
        autosave.Status.SavedOn.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    public async Task EveryKeystrokeRestartsTheCountdownRatherThanExtendingIt()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(AutosaveResult.Saved);
        });

        // Somebody writing a long paragraph, pausing to think, and carrying on.
        autosave.Touch();
        clock.Advance(TimeSpan.FromSeconds(15));
        autosave.Touch();
        clock.Advance(TimeSpan.FromSeconds(15));

        attempts.Should().Be(
            0,
            "thirty seconds have passed but the typing never stopped for twenty of them");

        clock.Advance(TimeSpan.FromSeconds(5));
        await autosave.Pending;

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task AScreenNobodyTypesIntoIsNeverSaved()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(AutosaveResult.Saved);
        });

        clock.Advance(TimeSpan.FromMinutes(5));
        await autosave.Pending;

        attempts.Should().Be(
            0,
            "an editor who only looked at a page must not move its modified date or give it " +
            "unpublished changes");
    }

    [Fact]
    public async Task ATransientFailureIsRetriedAndTheLatestTextIsWhatGetsSaved()
    {
        var clock = new FakeTimeProvider(Now);
        var typed = "first draft";
        var saved = new List<string>();
        var fail = true;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            saved.Add(typed);

            if (!fail) return Task.FromResult(AutosaveResult.Saved);

            fail = false;

            return Task.FromResult(new AutosaveResult(AutosaveOutcome.Transient, "The network went away."));
        });

        autosave.Touch();
        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        autosave.Status.Phase.Should().Be(AutosavePhase.Retrying);
        autosave.Status.HasUnsavedWork.Should().BeTrue("the edit is still held, and still unsaved");

        // The editor keeps writing while the connection is down, which is exactly when a queue of
        // stale payloads would save the wrong thing.
        typed = "second draft";

        clock.Advance(TimeSpan.FromSeconds(2));
        await autosave.Pending;

        autosave.Status.Phase.Should().Be(AutosavePhase.Saved);
        saved.Should().Equal("first draft", "second draft");
    }

    [Fact]
    public async Task AnExceptionFromTheSaveIsTreatedAsTransientRatherThanSwallowed()
    {
        var clock = new FakeTimeProvider(Now);

        await using var autosave = new AutosaveController(
            clock,
            _ => throw new HttpRequestException("Failed to fetch."));

        autosave.Touch();
        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        autosave.Status.Phase.Should().Be(
            AutosavePhase.Retrying,
            "an autosave that died on an unobserved exception would leave the editor typing into a " +
            "screen that has quietly stopped saving");
        autosave.Status.Message.Should().Contain("Failed to fetch");
    }

    [Fact]
    public async Task ARefusalIsReportedAndNotRetried()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(new AutosaveResult(
                AutosaveOutcome.Refused,
                "Somebody else saved this page first."));
        });

        autosave.Touch();
        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        clock.Advance(TimeSpan.FromMinutes(2));
        await autosave.Pending;

        attempts.Should().Be(
            1,
            "repeating a request the server has already reasoned about buries the message that " +
            "explains it, and a conflict needs a decision from a person");
        autosave.Status.Phase.Should().Be(AutosavePhase.Refused);
        autosave.Status.Message.Should().Be("Somebody else saved this page first.");
    }

    [Fact]
    public async Task TypingAgainRetiresARefusalAndReschedules()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(attempts == 1
                ? new AutosaveResult(AutosaveOutcome.Refused, "That zone is required.")
                : AutosaveResult.Saved);
        });

        autosave.Touch();
        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        autosave.Touch();

        autosave.Status.Phase.Should().Be(AutosavePhase.Pending);
        autosave.Status.Message.Should().BeNull("that refusal was about text that no longer exists");

        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        attempts.Should().Be(2);
        autosave.Status.Phase.Should().Be(AutosavePhase.Saved);
    }

    [Fact]
    public async Task LeavingTheScreenSavesWithoutWaitingForTheCountdown()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(AutosaveResult.Saved);
        });

        autosave.Touch();

        await autosave.FlushAsync();

        attempts.Should().Be(1, "navigating away is the other half of spec section 11.3's autosave");
        autosave.Status.Phase.Should().Be(AutosavePhase.Saved);

        // And the countdown that was running must not fire a second write for the same text.
        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task LeavingAScreenWithNothingToSaveWritesNothing()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(AutosaveResult.Saved);
        });

        await autosave.FlushAsync();

        attempts.Should().Be(0);
    }

    [Fact]
    public async Task WorkSavedBySomethingElseCancelsThePendingWrite()
    {
        var clock = new FakeTimeProvider(Now);
        var attempts = 0;

        await using var autosave = new AutosaveController(clock, _ =>
        {
            attempts++;

            return Task.FromResult(AutosaveResult.Saved);
        });

        autosave.Touch();

        // A publish, a discard, or a reload all leave the server holding what the screen holds.
        autosave.MarkSaved(clock.GetUtcNow());

        clock.Advance(AutosaveController.IdleDelay);
        await autosave.Pending;

        attempts.Should().Be(0);
        autosave.Status.HasUnsavedWork.Should().BeFalse();
    }
}
