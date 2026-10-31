using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

public class ErrorLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string LogPath => Path.Combine(_directory, "error.log");

    [Fact]
    public void RecordsWhereAndTheWholeException()
    {
        ErrorLog.Write("tray UI thread", new InvalidOperationException("boom"), LogPath);

        var text = File.ReadAllText(LogPath);
        Assert.Contains("[tray UI thread]", text);
        Assert.Contains("System.InvalidOperationException: boom", text);
    }

    [Fact]
    public void AppendsRatherThanReplacing()
    {
        ErrorLog.Write("first", new InvalidOperationException("one"), LogPath);
        ErrorLog.Write("second", new InvalidOperationException("two"), LogPath);

        var text = File.ReadAllText(LogPath);
        Assert.Contains("one", text);
        Assert.Contains("two", text);
    }

    /// <summary>A fault in a loop must not fill the disk.</summary>
    [Fact]
    public void RollsOverPastTheCap()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(LogPath, new string('x', (1024 * 1024) + 1));

        ErrorLog.Write("after the cap", new InvalidOperationException("fresh"), LogPath);

        Assert.True(File.Exists(LogPath + ".old"));
        Assert.DoesNotContain("xxxx", File.ReadAllText(LogPath));
    }

    [Fact]
    public void NeverThrowsWhenItCannotWrite()
    {
        Directory.CreateDirectory(_directory);
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        ErrorLog.Write("unwritable", new InvalidOperationException("lost"), Path.Combine(blocker, "error.log"));
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
