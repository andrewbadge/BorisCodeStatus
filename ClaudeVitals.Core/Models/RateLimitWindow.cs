using System.Text.Json.Serialization;

namespace ClaudeVitals.Core.Models;

/// <summary>One quota window (five-hour session or seven-day week) as reported by statusLine.</summary>
public sealed record RateLimitWindow
{
    [JsonPropertyName("used_percentage")]
    public double? UsedPercentage { get; init; }

    [JsonPropertyName("resets_at")]
    [JsonConverter(typeof(FlexibleDateTimeOffsetConverter))]
    public DateTimeOffset? ResetsAt { get; init; }

    /// <summary>Whole minutes until this window resets, or null if unknown. Convenience for the ESP32.</summary>
    [JsonPropertyName("resets_in_minutes")]
    public int? ResetsInMinutes =>
        ResetsAt is { } r ? Math.Max(0, (int)Math.Round((r - DateTimeOffset.UtcNow).TotalMinutes)) : null;
}
