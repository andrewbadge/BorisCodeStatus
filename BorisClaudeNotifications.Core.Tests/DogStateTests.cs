using BorisClaudeNotifications.Core.Models;
using BorisClaudeNotifications.Core.State;

namespace BorisClaudeNotifications.Core.Tests;

/// <summary>
/// Covers which dog pose a given state produces. Shared by the tray icon and (eventually) the
/// FidoESP32 firmware, so the rule is worth pinning down in one place.
/// </summary>
public class DogStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A state with a live session and the given activity, changed just now.</summary>
    private static VitalsState Active(ActivityState activity) => new()
    {
        Activity = activity,
        ActivityChangedUtc = Now,
        LastEventUtc = Now,
    };

    [Theory]
    [InlineData(ActivityState.Working, DogState.Working)]
    [InlineData(ActivityState.Waiting, DogState.Waiting)]
    [InlineData(ActivityState.Idle, DogState.Idle)]
    public void ActivityPicksThePose(ActivityState activity, DogState expected)
    {
        Assert.Equal(expected, DogStates.For(Active(activity), Now));
    }

    [Fact]
    public void SleepsWithNothingSeenYet()
    {
        Assert.Equal(DogState.Sleeping, DogStates.For(new VitalsState(), Now));
    }

    [Fact]
    public void StaysAwakeWhileIdleIsRecent()
    {
        var state = Active(ActivityState.Idle) with { ActivityChangedUtc = Now - TimeSpan.FromMinutes(4) };

        Assert.Equal(DogState.Idle, DogStates.For(state, Now));
    }

    [Fact]
    public void SleepsOnceIdleOutlastsTheSleepDelay()
    {
        var state = Active(ActivityState.Idle) with
        {
            ActivityChangedUtc = Now - DogStates.SleepAfter - TimeSpan.FromSeconds(1),
        };

        Assert.Equal(DogState.Sleeping, DogStates.For(state, Now));
    }

    /// <summary>Working has no sleep timer: a long tool run must not put the dog to bed.</summary>
    [Fact]
    public void KeepsWorkingHoweverLongTheTurnRuns()
    {
        var state = Active(ActivityState.Working) with { ActivityChangedUtc = Now - TimeSpan.FromHours(2) };

        Assert.Equal(DogState.Working, DogStates.For(state, Now));
    }

    /// <summary>Stop fires before SessionEnd, so the last activity of every session is Idle.</summary>
    [Fact]
    public void AClosedSessionSleepsEvenThoughItsLastActivityWasIdle()
    {
        var ended = VitalsUpdates.ApplySessionLifetime(Active(ActivityState.Idle), ended: true);

        Assert.Equal(ActivityState.Idle, ended.Activity);
        Assert.Equal(DogState.Sleeping, DogStates.For(ended, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ADeadSessionSleepsWhateverTheActivitySaid()
    {
        // Working, but nothing has been heard for longer than the session idle timeout: the
        // process was killed mid-turn and Stop never fired.
        var abandoned = new VitalsState
        {
            Activity = ActivityState.Working,
            ActivityChangedUtc = DateTimeOffset.UtcNow - TimeSpan.FromHours(1),
            LastEventUtc = DateTimeOffset.UtcNow - VitalsState.SessionIdleTimeout - TimeSpan.FromMinutes(1),
        };

        Assert.Equal(SessionStatus.Inactive, abandoned.SessionStatus);
        Assert.Equal(DogState.Sleeping, DogStates.For(abandoned, DateTimeOffset.UtcNow));
    }

    /// <summary>The icon settles well before the API calls the session dead; they are not the same clock.</summary>
    [Fact]
    public void TheDogSleepsSoonerThanSessionStatusGivesUp()
    {
        Assert.True(DogStates.SleepAfter < VitalsState.SessionIdleTimeout);
    }

    /// <summary>
    /// The whole rule must answer for the injected instant, not partly for wall-clock time.
    /// This state is minutes old by its own clock and more than a year old by the real one; if
    /// session status were judged against UtcNow it would read Inactive and the dog would sleep.
    /// Written after exactly that mix made these tests pass one day and fail the next.
    /// </summary>
    [Fact]
    public void JudgesSessionStatusAgainstTheInjectedClock()
    {
        var longAgo = new DateTimeOffset(2020, 1, 1, 9, 0, 0, TimeSpan.Zero);
        var state = new VitalsState
        {
            Activity = ActivityState.Working,
            ActivityChangedUtc = longAgo,
            LastEventUtc = longAgo,
        };

        Assert.Equal(DogState.Working, DogStates.For(state, longAgo.AddMinutes(1)));
        Assert.Equal(DogState.Sleeping, DogStates.For(state, longAgo.AddHours(1)));
    }
}
