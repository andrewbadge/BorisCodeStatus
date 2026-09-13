using ClaudeVitals.Core.Models;
using ClaudeVitals.Core.State;

namespace ClaudeVitals.Core.Tests;

/// <summary>Covers the lifecycle-hook verb mapping and how activity transitions are stamped.</summary>
public class ActivityTests
{
    [Theory]
    [InlineData("notification", ActivityState.Waiting)]
    [InlineData("Notification", ActivityState.Waiting)]
    [InlineData("stop", ActivityState.Idle)]
    [InlineData("  STOP  ", ActivityState.Idle)]
    [InlineData("pretooluse", ActivityState.Working)]
    [InlineData("userpromptsubmit", ActivityState.Working)]
    public void VerbMapsToActivity(string verb, ActivityState expected)
    {
        Assert.Equal(expected, VitalsUpdates.ActivityForVerb(verb));
    }

    [Theory]
    [InlineData("statusline")]
    [InlineData("nonsense")]
    [InlineData(null)]
    public void NonLifecycleVerbsMapToNothing(string? verb)
    {
        Assert.Null(VitalsUpdates.ActivityForVerb(verb));
    }

    [Fact]
    public void ParsesLifecycleHookPayload()
    {
        var hook = VitalsUpdates.TryParse<HookEvent>("""
        {
          "session_id": "abc-123",
          "hook_event_name": "Notification",
          "message": "Claude needs your permission to use Bash"
        }
        """);

        Assert.Equal("abc-123", hook?.SessionId);
        Assert.Equal("Notification", hook?.HookEventName);
    }

    [Fact]
    public void ActivityChangeStampsTimestamp()
    {
        var idle = VitalsUpdates.ApplyActivity(new VitalsState(), ActivityState.Idle);
        Assert.Equal(ActivityState.Idle, idle.Activity);
        Assert.NotNull(idle.ActivityChangedUtc);

        var working = VitalsUpdates.ApplyActivity(idle, ActivityState.Working);
        Assert.True(working.ActivityChangedUtc > idle.ActivityChangedUtc);
    }

    [Fact]
    public void RepeatedSameActivityDoesNotResetTimestamp()
    {
        // PreToolUse fires per tool call; the display should show how long work has run overall.
        var first = VitalsUpdates.ApplyActivity(new VitalsState(), ActivityState.Working);
        var second = VitalsUpdates.ApplyActivity(first, ActivityState.Working);

        Assert.Equal(first.ActivityChangedUtc, second.ActivityChangedUtc);
    }

    [Fact]
    public void ActivityCarriesSessionIdWithoutErasingIt()
    {
        var withSession = VitalsUpdates.ApplyActivity(
            new VitalsState(),
            ActivityState.Working,
            new HookEvent { SessionId = "sess-1" });
        Assert.Equal("sess-1", withSession.SessionId);

        var noSession = VitalsUpdates.ApplyActivity(withSession, ActivityState.Idle, new HookEvent());
        Assert.Equal("sess-1", noSession.SessionId);
    }
}

/// <summary>
/// Exercises the cross-process contract directly: separate store instances over one file stand in
/// for the hook process and the tray process, which is exactly how the two really communicate.
/// </summary>
public class VitalsStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClaudeVitalsTests", Guid.NewGuid().ToString("N"));

    private string StatePath => Path.Combine(_directory, "state.json");

    [Fact]
    public void UpdatePersistsAcrossStoreInstances()
    {
        // This is the whole cross-process contract: the hook process writes, the tray process reads.
        using (var writer = new VitalsStateStore(StatePath))
        {
            writer.Update(s => s with { ModelDisplayName = "Opus 5", ContextUsedPercentage = 12.5 });
        }

        using var reader = new VitalsStateStore(StatePath);

        Assert.Equal("Opus 5", reader.Current.ModelDisplayName);
        Assert.Equal(12.5, reader.Current.ContextUsedPercentage);
    }

    [Fact]
    public void ReaderPicksUpAnotherProcessesWrite()
    {
        using var reader = new VitalsStateStore(StatePath);
        Assert.Equal(ActivityState.Unknown, reader.Current.Activity);

        using (var writer = new VitalsStateStore(StatePath))
        {
            writer.Update(s => VitalsUpdates.ApplyActivity(s, ActivityState.Waiting));
        }

        Assert.Equal(ActivityState.Waiting, reader.Current.Activity);
    }

    [Fact]
    public void UpdateStampsLastUpdated()
    {
        using var store = new VitalsStateStore(StatePath);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var updated = store.Update(s => s with { SessionCostUsd = 1 });

        Assert.True(updated.LastUpdatedUtc >= before);
        Assert.InRange(updated.AgeSeconds, 0, 5);
    }

    [Fact]
    public void MissingFileYieldsDefaultState()
    {
        using var store = new VitalsStateStore(Path.Combine(_directory, "does-not-exist.json"));

        Assert.Equal(ActivityState.Unknown, store.Current.Activity);
        Assert.Null(store.Current.Session);
    }

    [Fact]
    public void CorruptFileIsIgnoredRatherThanThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StatePath, "{ this is not json");

        using var store = new VitalsStateStore(StatePath);

        Assert.Equal(ActivityState.Unknown, store.Current.Activity);
    }

    [Fact]
    public void ConcurrentUpdatesAllLandWithoutCorruption()
    {
        using var store = new VitalsStateStore(StatePath);

        Parallel.For(0, 50, i => store.Update(s => s with { SessionCostUsd = i }));

        Assert.NotNull(store.Current.SessionCostUsd);
        Assert.Equal(ActivityState.Unknown, store.Current.Activity);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only.
        }

        GC.SuppressFinalize(this);
    }
}
