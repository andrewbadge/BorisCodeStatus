using System.Text.Json.Serialization;

namespace BorisCodeStatus.Core.Models;

/// <summary>
/// Everything the relay knows, and the exact payload served from <c>GET /status</c>.
/// Property names are snake_case so the ESP32 firmware can key off them directly.
/// </summary>
public sealed record VitalsState
{
    /// <summary>
    /// How long a session may go without any event before it is reported as
    /// <see cref="Models.SessionStatus.Inactive"/>. Generous on purpose: a gap while the user
    /// reads a long reply or steps away is still an open session, and flapping between Active
    /// and Inactive on the display would be worse than reacting slowly.
    /// </summary>
    public static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(15);

    /// <summary>Five-hour session quota. Null until a statusLine event has been seen.</summary>
    [JsonPropertyName("session")]
    public RateLimitWindow? Session { get; init; }

    /// <summary>Seven-day quota across all models.</summary>
    [JsonPropertyName("week")]
    public RateLimitWindow? Week { get; init; }

    /// <summary>
    /// Seven-day quota for the Sonnet-only pool. Not present in statusLine data — populated
    /// only from the fallback OAuth usage endpoint, so it may lag or stay null.
    /// </summary>
    [JsonPropertyName("week_sonnet")]
    public RateLimitWindow? WeekSonnet { get; init; }

    [JsonPropertyName("context_used_percentage")]
    public double? ContextUsedPercentage { get; init; }

    [JsonPropertyName("model_display_name")]
    public string? ModelDisplayName { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("session_name")]
    public string? SessionName { get; init; }

    /// <summary>Cost of the current session only, as reported by statusLine.</summary>
    [JsonPropertyName("session_cost_usd")]
    public double? SessionCostUsd { get; init; }

    [JsonPropertyName("session_duration_ms")]
    public long? SessionDurationMs { get; init; }

    /// <summary>
    /// Calendar-month spend derived locally from JSONL transcripts. Always null in v1 —
    /// no Claude Code data source exposes a month figure. See README.
    /// </summary>
    [JsonPropertyName("month_cost_usd")]
    public double? MonthCostUsd { get; init; }

    [JsonPropertyName("activity")]
    [JsonConverter(typeof(JsonStringEnumConverter<ActivityState>))]
    public ActivityState Activity { get; init; } = ActivityState.Unknown;

    /// <summary>When <see cref="Activity"/> last changed — lets the display time out a stale "working".</summary>
    [JsonPropertyName("activity_changed_utc")]
    public DateTimeOffset? ActivityChangedUtc { get; init; }

    /// <summary>
    /// What Claude Code is blocked on, from the Notification hook's own text — typically naming
    /// the tool awaiting permission. Null unless <see cref="Activity"/> is
    /// <see cref="ActivityState.Waiting"/>; it is cleared on the way out so a stale prompt cannot
    /// be shown against a later state.
    /// </summary>
    [JsonPropertyName("waiting_message")]
    public string? WaitingMessage { get; init; }

    /// <summary>
    /// When a Claude Code session event (statusLine or a lifecycle hook) was last seen.
    /// Distinct from <see cref="LastUpdatedUtc"/>, which also moves for background writes such
    /// as the usage-API refresh — those say nothing about whether a session is open.
    /// </summary>
    [JsonPropertyName("last_event_utc")]
    public DateTimeOffset? LastEventUtc { get; init; }

    /// <summary>When the SessionEnd hook last fired. Persisted so the status survives a restart.</summary>
    [JsonPropertyName("session_ended_utc")]
    public DateTimeOffset? SessionEndedUtc { get; init; }

    /// <summary>
    /// Whether a session is open, computed on read rather than stored: a session going quiet
    /// writes nothing to the state file, so a stored value would sit at "Active" forever.
    /// Same reason <see cref="AgeSeconds"/> is computed.
    /// </summary>
    [JsonPropertyName("session_status")]
    [JsonConverter(typeof(JsonStringEnumConverter<SessionStatus>))]
    public SessionStatus SessionStatus => SessionStatusAt(DateTimeOffset.UtcNow);

    /// <summary>
    /// <see cref="SessionStatus"/> judged against a given instant.
    ///
    /// Exists so callers that already have a clock — the dog-pose rule, and tests — can use one
    /// consistently. Mixing an injected time with <see cref="DateTimeOffset.UtcNow"/> inside the
    /// same decision is incoherent: it happens to agree at runtime, where both are "now", and
    /// disagrees everywhere else.
    /// </summary>
    public SessionStatus SessionStatusAt(DateTimeOffset now)
    {
        if (LastEventUtc is not { } lastEvent)
        {
            return SessionStatus.Unknown;
        }

        // SessionEnd stamps both timestamps with the same instant, so ">=" is what lets an
        // end win over the event that carried it; any later event moves LastEventUtc past it.
        if (SessionEndedUtc is { } ended && ended >= lastEvent)
        {
            return SessionStatus.Ended;
        }

        return now - lastEvent > SessionIdleTimeout
            ? SessionStatus.Inactive
            : SessionStatus.Active;
    }

    /// <summary>When any field was last written.</summary>
    [JsonPropertyName("last_updated_utc")]
    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>When the fallback usage API last succeeded, for diagnostics.</summary>
    [JsonPropertyName("usage_api_last_success_utc")]
    public DateTimeOffset? UsageApiLastSuccessUtc { get; init; }

    /// <summary>Seconds since <see cref="LastUpdatedUtc"/>, so the ESP32 can grey out stale data.</summary>
    [JsonPropertyName("age_seconds")]
    public int AgeSeconds => Math.Max(0, (int)Math.Round((DateTimeOffset.UtcNow - LastUpdatedUtc).TotalSeconds));
}
