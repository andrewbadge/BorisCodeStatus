using ClaudeVitals.Core.Models;
using ClaudeVitals.Core.State;

namespace ClaudeVitals.Core.Tests;

/// <summary>
/// Pins the statusLine contract against realistic payloads. These fixtures are the closest thing
/// this project has to a schema: Claude Code does not version the payload, so a change in shape
/// shows up here first.
/// </summary>
public class StatusLineParsingTests
{
    // Shaped after a real statusLine payload on a subscription session.
    private const string FullPayload = """
    {
      "session_id": "b7f0c2de-1b3a-4c55-9f11-2a8de4c19f00",
      "session_name": "vitals relay",
      "model": { "id": "claude-opus-5", "display_name": "Opus 5" },
      "cost": { "total_cost_usd": 1.2345, "total_duration_ms": 843000 },
      "context_window": { "used_percentage": 37.5 },
      "rate_limits": {
        "five_hour": { "used_percentage": 42.0, "resets_at": "2026-09-13T19:00:00Z" },
        "seven_day": { "used_percentage": 61.25, "resets_at": "2026-09-17T04:30:00Z" }
      }
    }
    """;

    [Fact]
    public void ParsesEveryDocumentedField()
    {
        var status = VitalsUpdates.TryParse<StatusLineEvent>(FullPayload);

        Assert.NotNull(status);
        Assert.Equal("b7f0c2de-1b3a-4c55-9f11-2a8de4c19f00", status.SessionId);
        Assert.Equal("vitals relay", status.SessionName);
        Assert.Equal("Opus 5", status.Model?.DisplayName);
        Assert.Equal(1.2345, status.Cost?.TotalCostUsd);
        Assert.Equal(843000, status.Cost?.TotalDurationMs);
        Assert.Equal(37.5, status.ContextWindow?.UsedPercentage);
        Assert.Equal(42.0, status.RateLimits?.FiveHour?.UsedPercentage);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 13, 19, 0, 0, TimeSpan.Zero),
            status.RateLimits?.FiveHour?.ResetsAt);
        Assert.Equal(61.25, status.RateLimits?.SevenDay?.UsedPercentage);
    }

    [Fact]
    public void AppliesStatusLineOntoState()
    {
        var status = VitalsUpdates.TryParse<StatusLineEvent>(FullPayload)!;

        var state = VitalsUpdates.ApplyStatusLine(new VitalsState(), status);

        Assert.Equal(42.0, state.Session?.UsedPercentage);
        Assert.Equal(61.25, state.Week?.UsedPercentage);
        Assert.Equal(37.5, state.ContextUsedPercentage);
        Assert.Equal("Opus 5", state.ModelDisplayName);
        Assert.Equal(1.2345, state.SessionCostUsd);
    }

    [Fact]
    public void AcceptsEpochSecondsForResetsAt()
    {
        // resets_at is not contractually a string; accept the numeric form too.
        const string json = """
        { "rate_limits": { "five_hour": { "used_percentage": 5, "resets_at": 1789000000 } } }
        """;

        var status = VitalsUpdates.TryParse<StatusLineEvent>(json);

        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1789000000),
            status?.RateLimits?.FiveHour?.ResetsAt);
    }

    [Fact]
    public void AcceptsEpochMillisecondsForResetsAt()
    {
        const string json = """
        { "rate_limits": { "five_hour": { "resets_at": 1789000000000 } } }
        """;

        var status = VitalsUpdates.TryParse<StatusLineEvent>(json);

        Assert.Equal(
            DateTimeOffset.FromUnixTimeMilliseconds(1789000000000),
            status?.RateLimits?.FiveHour?.ResetsAt);
    }

    [Fact]
    public void MissingRateLimitsKeepsPreviousValues()
    {
        // API-key sessions omit rate_limits entirely; blanking the display would be worse than staleness.
        var seeded = VitalsUpdates.ApplyStatusLine(
            new VitalsState(),
            VitalsUpdates.TryParse<StatusLineEvent>(FullPayload)!);

        var withoutLimits = VitalsUpdates.TryParse<StatusLineEvent>("""
        { "model": { "display_name": "Haiku 4.5" }, "context_window": { "used_percentage": 3 } }
        """)!;

        var state = VitalsUpdates.ApplyStatusLine(seeded, withoutLimits);

        Assert.Equal(42.0, state.Session?.UsedPercentage);
        Assert.Equal(61.25, state.Week?.UsedPercentage);
        Assert.Equal("Haiku 4.5", state.ModelDisplayName);
        Assert.Equal(3, state.ContextUsedPercentage);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        var status = VitalsUpdates.TryParse<StatusLineEvent>("""
        { "workspace": { "current_dir": "C:\\GitHub" }, "version": "2.1.0", "model": { "display_name": "Opus 5" } }
        """);

        Assert.Equal("Opus 5", status?.Model?.DisplayName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{ \"model\": ")]
    public void MalformedInputReturnsNullRatherThanThrowing(string input)
    {
        Assert.Null(VitalsUpdates.TryParse<StatusLineEvent>(input));
    }

    [Fact]
    public void ResetsInMinutesCountsDownAndFloorsAtZero()
    {
        var future = new RateLimitWindow { ResetsAt = DateTimeOffset.UtcNow.AddMinutes(90) };
        var past = new RateLimitWindow { ResetsAt = DateTimeOffset.UtcNow.AddMinutes(-10) };

        Assert.InRange(future.ResetsInMinutes!.Value, 89, 90);
        Assert.Equal(0, past.ResetsInMinutes);
        Assert.Null(new RateLimitWindow().ResetsInMinutes);
    }
}
