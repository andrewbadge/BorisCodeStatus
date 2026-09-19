using System.Text.Json.Serialization;

namespace FidoRelay.Core.Models;

/// <summary>
/// The JSON Claude Code pipes to a configured <c>statusLine</c> command on stdin.
/// Every member is optional: Claude Code omits sections that do not apply (for example
/// <c>rate_limits</c> is absent on API-key sessions), and unknown members are ignored.
/// </summary>
public sealed record StatusLineEvent
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("session_name")]
    public string? SessionName { get; init; }

    [JsonPropertyName("model")]
    public StatusLineModel? Model { get; init; }

    [JsonPropertyName("cost")]
    public StatusLineCost? Cost { get; init; }

    [JsonPropertyName("context_window")]
    public StatusLineContextWindow? ContextWindow { get; init; }

    [JsonPropertyName("rate_limits")]
    public StatusLineRateLimits? RateLimits { get; init; }
}

public sealed record StatusLineModel
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

public sealed record StatusLineCost
{
    [JsonPropertyName("total_cost_usd")]
    public double? TotalCostUsd { get; init; }

    [JsonPropertyName("total_duration_ms")]
    public long? TotalDurationMs { get; init; }
}

public sealed record StatusLineContextWindow
{
    [JsonPropertyName("used_percentage")]
    public double? UsedPercentage { get; init; }
}

public sealed record StatusLineRateLimits
{
    [JsonPropertyName("five_hour")]
    public RateLimitWindow? FiveHour { get; init; }

    [JsonPropertyName("seven_day")]
    public RateLimitWindow? SevenDay { get; init; }
}
