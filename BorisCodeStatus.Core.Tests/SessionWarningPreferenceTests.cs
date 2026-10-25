using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The session warning threshold: 80 by default, otherwise a percentage in range.</summary>
public class SessionWarningPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "session-warning.txt");

    public SessionWarningPreferenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void EightyPercentByDefault()
    {
        Assert.Equal(80, SessionWarningPreference.Get(FilePath));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void RemembersTheChosenThreshold()
    {
        Assert.True(SessionWarningPreference.TrySet(65, FilePath));

        Assert.Equal(65, SessionWarningPreference.Get(FilePath));
    }

    [Fact]
    public void SettingTheDefaultRemovesTheFile()
    {
        SessionWarningPreference.TrySet(65, FilePath);
        SessionWarningPreference.TrySet(SessionWarningPreference.Default, FilePath);

        Assert.False(File.Exists(FilePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("10")]
    [InlineData("250")]
    public void GarbageOrOutOfRangeIsTheDefault(string content)
    {
        File.WriteAllText(FilePath, content);

        Assert.Equal(80, SessionWarningPreference.Get(FilePath));
    }

    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(SessionWarningPreference.TrySet(65, Path.Combine(blocker, "file")));
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
