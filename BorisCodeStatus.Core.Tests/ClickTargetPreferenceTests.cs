using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The waiting card's click target: off (dismiss only) by default, otherwise a process name.</summary>
public class ClickTargetPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "card-click-app.txt");

    public ClickTargetPreferenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void DismissOnlyByDefault()
    {
        Assert.Null(ClickTargetPreference.Get(FilePath));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void RemembersTheChosenApp()
    {
        Assert.True(ClickTargetPreference.TrySet("WindowsTerminal", FilePath));

        Assert.Equal("WindowsTerminal", ClickTargetPreference.Get(FilePath));
    }

    [Fact]
    public void ClearingReturnsToTheDefault()
    {
        ClickTargetPreference.TrySet("chrome", FilePath);
        ClickTargetPreference.TrySet(null, FilePath);

        Assert.Null(ClickTargetPreference.Get(FilePath));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void AnEmptyFileIsTheDefault()
    {
        File.WriteAllText(FilePath, "  \r\n");

        Assert.Null(ClickTargetPreference.Get(FilePath));
    }

    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(ClickTargetPreference.TrySet("chrome", Path.Combine(blocker, "file")));
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
