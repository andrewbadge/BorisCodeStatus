namespace FidoRelay.Core.Models;

/// <summary>
/// Which of the four 8-bit dog poses represents the session right now.
///
/// Presentation rather than data, but it lives in Core deliberately: the tray icon and the
/// FidoESP32 display must agree on what the dog is doing, and the only way to guarantee that is
/// for both to derive it from one rule rather than each inventing their own.
/// </summary>
public enum DogState
{
    /// <summary>Curled up. No session, or an idle one that has gone quiet.</summary>
    Sleeping = 0,

    /// <summary>Sitting, alert, green. A turn has finished and Claude is waiting on nothing.</summary>
    Idle = 1,

    /// <summary>Running. Claude is generating or running a tool.</summary>
    Working = 2,

    /// <summary>Ears up, amber. Claude is blocked on the user, e.g. a permission prompt.</summary>
    Waiting = 3,
}

/// <summary>Maps <see cref="VitalsState"/> onto the dog pose.</summary>
public static class DogStates
{
    /// <summary>
    /// How long <see cref="ActivityState.Idle"/> must hold before the dog falls asleep.
    ///
    /// Deliberately shorter than <see cref="VitalsState.SessionIdleTimeout"/>, which governs the
    /// <c>session_status</c> field. They answer different questions: the icon is ambient and can
    /// afford to settle after a few quiet minutes, whereas the API field is what the panel keys
    /// off and should not claim a session is over while the user is merely reading a reply.
    /// </summary>
    public static readonly TimeSpan SleepAfter = TimeSpan.FromMinutes(5);

    /// <param name="now">Injected so the transition is testable without waiting five minutes.</param>
    public static DogState For(VitalsState state, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(state);

        // A closed or long-dead session outranks whatever the last activity happened to be:
        // Stop fires before SessionEnd, so the final activity of every session is Idle.
        // Judged against the same instant as the idle check below, rather than wall-clock time,
        // so the whole rule answers for one moment.
        if (state.SessionStatusAt(now) is SessionStatus.Ended or SessionStatus.Inactive or SessionStatus.Unknown)
        {
            return DogState.Sleeping;
        }

        return state.Activity switch
        {
            ActivityState.Working => DogState.Working,
            ActivityState.Waiting => DogState.Waiting,
            ActivityState.Idle => HasBeenIdleLongEnoughToSleep(state, now) ? DogState.Sleeping : DogState.Idle,
            _ => DogState.Sleeping,
        };
    }

    private static bool HasBeenIdleLongEnoughToSleep(VitalsState state, DateTimeOffset now) =>
        // No timestamp means the state was never observed changing, which is not evidence of a
        // recent turn — err towards asleep rather than showing a falsely alert dog forever.
        state.ActivityChangedUtc is not { } changed || now - changed >= SleepAfter;
}
