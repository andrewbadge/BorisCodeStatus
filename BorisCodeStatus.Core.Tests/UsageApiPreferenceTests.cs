using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>
/// Covers the opt-in for the usage-API fallback: off by default, remembered across a restart, and
/// the fields it fills going null when it is off.
/// </summary>
public class UsageApiPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string FlagPath => Path.Combine(_directory, "usage-api-enabled.flag");

    public UsageApiPreferenceTests() => Directory.CreateDirectory(_directory);

    /// <summary>
    /// The default has to hold with nothing on disk: an install nobody configured must never read
    /// the OAuth token or call out.
    /// </summary>
    [Fact]
    public void DisabledByDefault()
    {
        Assert.False(UsageApiPreference.IsEnabled(FlagPath));
        Assert.False(File.Exists(FlagPath));
    }

    [Fact]
    public void RemembersBeingEnabled()
    {
        Assert.True(UsageApiPreference.TrySetEnabled(true, FlagPath));

        // A separate read is the point: this is what the next poll and the next process start do.
        Assert.True(UsageApiPreference.IsEnabled(FlagPath));
    }

    [Fact]
    public void RemembersBeingDisabledAgain()
    {
        UsageApiPreference.TrySetEnabled(true, FlagPath);
        Assert.True(UsageApiPreference.TrySetEnabled(false, FlagPath));

        Assert.False(UsageApiPreference.IsEnabled(FlagPath));
    }

    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        // A path whose parent is a file, not a directory — unwritable without being exotic.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(UsageApiPreference.TrySetEnabled(true, Path.Combine(blocker, "usage-api-enabled.flag")));
    }

    /// <summary>
    /// What /status serves while the fallback is off: its two fields null, everything the hooks
    /// supply untouched.
    /// </summary>
    [Fact]
    public void WithoutUsageApiFieldsNullsOnlyTheFallbackFields()
    {
        var week = new RateLimitWindow { UsedPercentage = 61.25 };
        var state = new VitalsState
        {
            Week = week,
            WeekSonnet = new RateLimitWindow { UsedPercentage = 12 },
            UsageApiLastSuccessUtc = DateTimeOffset.UtcNow,
            SessionName = "kept",
        };

        var stripped = state.WithoutUsageApiFields();

        Assert.Null(stripped.WeekSonnet);
        Assert.Null(stripped.UsageApiLastSuccessUtc);
        Assert.Equal(week, stripped.Week);
        Assert.Equal("kept", stripped.SessionName);
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
