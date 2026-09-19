using System.Text.Json;
using ClaudeVitals.Core.Models;
using ClaudeVitals.Core.State;

namespace ClaudeVitals.Core.Tests;

/// <summary>
/// Covers session_status: the "is a session open" signal that sits alongside activity.
/// The distinction these tests exist to protect is that an idle turn is still an open session.
/// </summary>
public class SessionStatusTests
{
    [Theory]
    [InlineData("sessionstart", false)]
    [InlineData("SessionStart", false)]
    [InlineData("  sessionend  ", true)]
    public void VerbMapsToSessionLifetime(string verb, bool expectedEnded)
    {
        Assert.Equal(expectedEnded, VitalsUpdates.SessionLifetimeForVerb(verb));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("statusline")]
    [InlineData(null)]
    public void NonSessionVerbsMapToNothing(string? verb)
    {
        Assert.Null(VitalsUpdates.SessionLifetimeForVerb(verb));
    }

    [Fact]
    public void UnknownBeforeAnyEvent()
    {
        Assert.Equal(SessionStatus.Unknown, new VitalsState().SessionStatus);
    }

    [Fact]
    public void ActiveAfterSessionStart()
    {
        var state = VitalsUpdates.ApplySessionLifetime(new VitalsState(), ended: false);

        Assert.Equal(SessionStatus.Active, state.SessionStatus);
        Assert.Null(state.SessionEndedUtc);
    }

    [Fact]
    public void EndedAfterSessionEnd()
    {
        var state = VitalsUpdates.ApplySessionLifetime(new VitalsState(), ended: true);

        Assert.Equal(SessionStatus.Ended, state.SessionStatus);
    }

    [Fact]
    public void SessionStartClearsAPreviousEnd()
    {
        var ended = VitalsUpdates.ApplySessionLifetime(new VitalsState(), ended: true);
        var restarted = VitalsUpdates.ApplySessionLifetime(ended, ended: false);

        Assert.Equal(SessionStatus.Active, restarted.SessionStatus);
    }

    /// <summary>The whole point of the field: Stop sets activity Idle, but the session is still open.</summary>
    [Fact]
    public void IdleTurnIsStillAnActiveSession()
    {
        var state = VitalsUpdates.ApplyActivity(new VitalsState(), ActivityState.Idle);

        Assert.Equal(ActivityState.Idle, state.Activity);
        Assert.Equal(SessionStatus.Active, state.SessionStatus);
    }

    /// <summary>A session that dies without firing SessionEnd must decay rather than stay Active.</summary>
    [Fact]
    public void DecaysToInactiveOnceTheIdleTimeoutPasses()
    {
        var stale = new VitalsState
        {
            LastEventUtc = DateTimeOffset.UtcNow - VitalsState.SessionIdleTimeout - TimeSpan.FromMinutes(1),
        };

        Assert.Equal(SessionStatus.Inactive, stale.SessionStatus);
    }

    [Fact]
    public void AnEventJustInsideTheTimeoutIsStillActive()
    {
        var recent = new VitalsState
        {
            LastEventUtc = DateTimeOffset.UtcNow - VitalsState.SessionIdleTimeout + TimeSpan.FromMinutes(1),
        };

        Assert.Equal(SessionStatus.Active, recent.SessionStatus);
    }

    [Fact]
    public void StatusLineEventCountsAsSessionActivity()
    {
        var status = VitalsUpdates.TryParse<StatusLineEvent>("""{"session_id":"abc"}""");
        var state = VitalsUpdates.ApplyStatusLine(new VitalsState(), status!);

        Assert.Equal(SessionStatus.Active, state.SessionStatus);
    }

    /// <summary>session_status is computed, so it must serialise out but not break reading back in.</summary>
    [Fact]
    public void SerialisesAsAStringAndRoundTrips()
    {
        var state = VitalsUpdates.ApplySessionLifetime(new VitalsState(), ended: false);

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var json = JsonSerializer.Serialize(state, options);
        Assert.Contains("\"session_status\": \"Active\"", json);

        var restored = JsonSerializer.Deserialize<VitalsState>(json, options);
        Assert.Equal(SessionStatus.Active, restored!.SessionStatus);
    }
}
