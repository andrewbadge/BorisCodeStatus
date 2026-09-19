using FidoRelay.Core.State;

namespace FidoRelay.Core.Tests;

/// <summary>Covers the pause preference surviving a restart, and failing safe when it cannot.</summary>
public class PausePreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FidoRelayTests", Guid.NewGuid().ToString("N"));

    private string FlagPath => Path.Combine(_directory, "http-paused.flag");

    public PausePreferenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void DefaultsToRunning()
    {
        Assert.False(PausePreference.IsPaused(FlagPath));
    }

    [Fact]
    public void RemembersAPause()
    {
        Assert.True(PausePreference.TrySet(true, FlagPath));

        // A separate read is the point: this is what the next process start does.
        Assert.True(PausePreference.IsPaused(FlagPath));
    }

    [Fact]
    public void RemembersAResume()
    {
        PausePreference.TrySet(true, FlagPath);
        Assert.True(PausePreference.TrySet(false, FlagPath));

        Assert.False(PausePreference.IsPaused(FlagPath));
    }

    [Fact]
    public void ResumingWhenNotPausedIsHarmless()
    {
        Assert.True(PausePreference.TrySet(false, FlagPath));
        Assert.False(PausePreference.IsPaused(FlagPath));
    }

    [Fact]
    public void PausingTwiceStaysPaused()
    {
        PausePreference.TrySet(true, FlagPath);
        PausePreference.TrySet(true, FlagPath);

        Assert.True(PausePreference.IsPaused(FlagPath));
    }

    /// <summary>
    /// Failing to write must not throw: the pause has already taken effect in the running process
    /// by the time this is called, and losing the preference is the lesser problem.
    /// </summary>
    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        // A path whose parent is a file, not a directory — unwritable without being exotic.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(PausePreference.TrySet(true, Path.Combine(blocker, "http-paused.flag")));
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
