using System.Text.Json;
using ClaudeVitals.Core.Models;

namespace ClaudeVitals.Core.State;

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

        return current with
        {
            Activity = activity,
            ActivityChangedUtc = current.Activity == activity
                ? current.ActivityChangedUtc ?? DateTimeOffset.UtcNow
                : DateTimeOffset.UtcNow,
            SessionId = Coalesce(hook?.SessionId, current.SessionId),
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

    private static string? Coalesce(string? incoming, string? existing) =>
        string.IsNullOrWhiteSpace(incoming) ? existing : incoming;
}
