using System.Text.Json.Serialization;

namespace ClaudeVitals.Core.Models;

/// <summary>
/// Everything the relay knows, and the exact payload served from <c>GET /status</c>.
/// Property names are snake_case so the ESP32 firmware can key off them directly.
/// </summary>
public sealed record VitalsState
{
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
