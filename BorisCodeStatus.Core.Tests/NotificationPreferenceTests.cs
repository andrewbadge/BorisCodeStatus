using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>
/// Covers the notification setting and the waiting message the notification shows.
/// The setting is on by default, so the marker file means "disabled" — the inverse of pause.
/// </summary>
public class NotificationPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string FlagPath => Path.Combine(_directory, "notifications-disabled.flag");

    public NotificationPreferenceTests() => Directory.CreateDirectory(_directory);

    /// <summary>The default has to hold with nothing on disk — there is no first-run write.</summary>
    [Fact]
    public void EnabledByDefault()
    {
        Assert.True(NotificationPreference.AreEnabled(FlagPath));
        Assert.False(File.Exists(FlagPath));
    }

    [Fact]
    public void RemembersBeingDisabled()
    {
        Assert.True(NotificationPreference.TrySetEnabled(false, FlagPath));

        // A separate read is the point: this is what the next process start does.
        Assert.False(NotificationPreference.AreEnabled(FlagPath));
    }

    [Fact]
    public void RemembersBeingReEnabled()
    {
        NotificationPreference.TrySetEnabled(false, FlagPath);
        NotificationPreference.TrySetEnabled(true, FlagPath);

        Assert.True(NotificationPreference.AreEnabled(FlagPath));
        Assert.False(File.Exists(FlagPath));
    }

    [Fact]
    public void TogglingIsIdempotent()
    {
        NotificationPreference.TrySetEnabled(false, FlagPath);
        NotificationPreference.TrySetEnabled(false, FlagPath);
        Assert.False(NotificationPreference.AreEnabled(FlagPath));

        NotificationPreference.TrySetEnabled(true, FlagPath);
        NotificationPreference.TrySetEnabled(true, FlagPath);
        Assert.True(NotificationPreference.AreEnabled(FlagPath));
    }

    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(NotificationPreference.TrySetEnabled(false, Path.Combine(blocker, "flag")));
    }

    /// <summary>The notification quotes the hook's own text, so it has to be captured.</summary>
    [Fact]
    public void WaitingCapturesTheHookMessage()
    {
        var hook = VitalsUpdates.TryParse<HookEvent>("""
        { "hook_event_name": "Notification", "message": "Claude needs your permission to use Bash" }
        """);

        var state = VitalsUpdates.ApplyActivity(new VitalsState(), ActivityState.Waiting, hook);

        Assert.Equal("Claude needs your permission to use Bash", state.WaitingMessage);
    }

    /// <summary>A stale prompt must not be shown against a later state.</summary>
    [Fact]
    public void LeavingWaitingClearsTheMessage()
    {
        var hook = VitalsUpdates.TryParse<HookEvent>("""{ "message": "Permission needed" }""");
        var waiting = VitalsUpdates.ApplyActivity(new VitalsState(), ActivityState.Waiting, hook);

        var working = VitalsUpdates.ApplyActivity(waiting, ActivityState.Working);

        Assert.Null(working.WaitingMessage);
    }

    /// <summary>A second Waiting event with no text of its own keeps the message it had.</summary>
    [Fact]
    public void StayingInWaitingKeepsAnEarlierMessage()
    {
        var hook = VitalsUpdates.TryParse<HookEvent>("""{ "message": "Permission needed" }""");
        var first = VitalsUpdates.ApplyActivity(new VitalsState(), ActivityState.Waiting, hook);

        var second = VitalsUpdates.ApplyActivity(first, ActivityState.Waiting);

        Assert.Equal("Permission needed", second.WaitingMessage);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp directory; leaving it behind is harmless.
        }
    }
}
