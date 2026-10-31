using System.Text.Json;
using BorisCodeStatus.Core.Models;

namespace BorisCodeStatus.Core.State;

/// <summary>
/// Pure mapping from a parsed Claude Code payload onto <see cref="VitalsState"/>.
/// Kept separate from <see cref="VitalsStateStore"/> so the mapping can be unit tested
/// without touching the filesystem.
/// </summary>
public static class VitalsUpdates
{
    private static readonly JsonSerializerOptions ParseOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Parses a payload Claude Code piped to stdin. Returns null for empty or malformed input
    /// rather than throwing — a hook that fails is worse than a hook that does nothing.
    /// </summary>
    public static T? TryParse<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, ParseOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Folds a statusLine payload into the state. Fields absent from the payload keep their
    /// previous value: Claude Code omits <c>rate_limits</c> on API-key sessions, and blanking
    /// the display in that case would be worse than showing slightly stale numbers.
    /// </summary>
    public static VitalsState ApplyStatusLine(VitalsState current, StatusLineEvent status)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(status);

        return current with
        {
            Session = status.RateLimits?.FiveHour ?? current.Session,
            Week = status.RateLimits?.SevenDay ?? current.Week,
            ContextUsedPercentage = status.ContextWindow?.UsedPercentage ?? current.ContextUsedPercentage,
            ModelDisplayName = Coalesce(status.Model?.DisplayName, current.ModelDisplayName),
            SessionId = Coalesce(status.SessionId, current.SessionId),
            SessionName = Coalesce(status.SessionName, current.SessionName),
            SessionCostUsd = status.Cost?.TotalCostUsd ?? current.SessionCostUsd,
            SessionDurationMs = status.Cost?.TotalDurationMs ?? current.SessionDurationMs,
            LastEventUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Folds a lifecycle hook payload into the state, setting the activity implied by
    /// <paramref name="activity"/>. <see cref="VitalsState.ActivityChangedUtc"/> only moves when
    /// the activity actually changes, so the display can show how long a state has held.
    /// </summary>
    public static VitalsState ApplyActivity(VitalsState current, ActivityState activity, HookEvent? hook = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        var now = DateTimeOffset.UtcNow;

        return current with
        {
            Activity = activity,
            ActivityChangedUtc = current.Activity == activity
                ? current.ActivityChangedUtc ?? now
                : now,
            SessionId = Coalesce(hook?.SessionId, current.SessionId),
            LastEventUtc = now,

            // Only Waiting carries a message, and only its own: leaving the state clears it, so a
            // permission prompt from ten minutes ago can never be shown against a later state.
            WaitingMessage = activity == ActivityState.Waiting
                ? Coalesce(hook?.Message, current.WaitingMessage)
                : null,
        };
    }

    /// <summary>
    /// Folds a SessionStart or SessionEnd hook into the state. Deliberately leaves
    /// <see cref="VitalsState.Activity"/> alone: session lifetime and turn activity are separate
    /// signals, and a session opening says nothing about whether Claude is generating.
    /// </summary>
    public static VitalsState ApplySessionLifetime(VitalsState current, bool ended, HookEvent? hook = null)
    {
        ArgumentNullException.ThrowIfNull(current);

        // One instant for both stamps: SessionStatus compares them, and two UtcNow reads would
        // make an end that lands a tick earlier than its own event lose the comparison.
        var now = DateTimeOffset.UtcNow;

        return current with
        {
            SessionId = Coalesce(hook?.SessionId, current.SessionId),
            LastEventUtc = now,
            // A start clears any previous end, so a new session is not reported as Ended.
            SessionEndedUtc = ended ? now : null,
        };
    }

    /// <summary>Maps the command-line verb each hook registration passes to the activity it means.</summary>
    public static ActivityState? ActivityForVerb(string? verb) => verb?.Trim().ToLowerInvariant() switch
    {
        "notification" => ActivityState.Waiting,
        "stop" => ActivityState.Idle,
        "pretooluse" or "userpromptsubmit" or "working" => ActivityState.Working,
        _ => null,
    };

    /// <summary>
    /// Maps a verb to a session-lifetime change: true for an end, false for a start,
    /// null when the verb is not a session-lifetime hook at all.
    /// </summary>
    public static bool? SessionLifetimeForVerb(string? verb) => verb?.Trim().ToLowerInvariant() switch
    {
        "sessionstart" => false,
        "sessionend" => true,
        _ => null,
    };

    private static string? Coalesce(string? incoming, string? existing) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming;
}
