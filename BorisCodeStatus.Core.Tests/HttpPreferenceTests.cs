using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>Covers the HTTP preference defaulting to off, surviving a restart, and failing safe.</summary>
public class HttpPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public HttpPreferenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void DefaultsToDisabled()
    {
        Assert.False(HttpPreference.IsEnabled(SettingsPath));
    }

    [Fact]
    public void RemembersEnabling()
    {
        Assert.True(HttpPreference.TrySetEnabled(true, SettingsPath));

        // A separate read is the point: this is what the next process start does.
        Assert.True(HttpPreference.IsEnabled(SettingsPath));
    }

    [Fact]
    public void RemembersDisabling()
    {
        HttpPreference.TrySetEnabled(true, SettingsPath);
        Assert.True(HttpPreference.TrySetEnabled(false, SettingsPath));

        Assert.False(HttpPreference.IsEnabled(SettingsPath));
    }

    [Fact]
    public void DisablingWhenAlreadyDisabledIsHarmless()
    {
        Assert.True(HttpPreference.TrySetEnabled(false, SettingsPath));
        Assert.False(HttpPreference.IsEnabled(SettingsPath));
    }

    [Fact]
    public void EnablingTwiceStaysEnabled()
    {
        HttpPreference.TrySetEnabled(true, SettingsPath);
        HttpPreference.TrySetEnabled(true, SettingsPath);

        Assert.True(HttpPreference.IsEnabled(SettingsPath));
    }

    /// <summary>
    /// The marker written by earlier versions meant "paused". It must not be mistaken for the new
    /// one: an old pause should read as disabled, which is also the new default.
    /// </summary>
    [Fact]
    public void LegacyPausedMarkerDoesNotEnable()
    {
        File.WriteAllText(Path.Combine(_directory, "http-paused.flag"), "paused");

        Assert.False(HttpPreference.IsEnabled(SettingsPath));
    }

    /// <summary>
    /// Failing to write must not throw: the change has already taken effect in the running process
    /// by the time this is called, and losing the preference is the lesser problem.
    /// </summary>
    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        // A path whose parent is a file, not a directory — unwritable without being exotic.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(HttpPreference.TrySetEnabled(true, Path.Combine(blocker, "http-enabled.flag")));
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
