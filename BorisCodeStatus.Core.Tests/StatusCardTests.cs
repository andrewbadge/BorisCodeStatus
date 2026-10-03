using System.Drawing;
using BorisCodeStatus.Core.State;
using BorisCodeStatus.Tray;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The pinned status card: its preference, its saved position, and keeping it on screen.</summary>
public class StatusCardTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private static readonly Rectangle Monitor = new(0, 0, 1920, 1040);
    private static readonly Size Card = new(354, 282);

    public StatusCardTests() => Directory.CreateDirectory(_directory);

    private string PinnedPath => Path.Combine(_directory, "status-card-pinned.flag");

    private string PositionPath => Path.Combine(_directory, "status-card-position.txt");

    [Fact]
    public void UnpinnedByDefault() => Assert.False(StatusCardPreference.IsPinned(PinnedPath));

    [Fact]
    public void RemembersBeingPinned()
    {
        Assert.True(StatusCardPreference.TrySetPinned(true, PinnedPath));
        Assert.True(StatusCardPreference.IsPinned(PinnedPath));
    }

    [Fact]
    public void RoundTripsAPositionIncludingNegativeCoordinates()
    {
        // A monitor left of the primary has negative x.
        Assert.True(StatusCardPreference.TrySavePosition(-1500, 200, PositionPath));
        Assert.Equal((-1500, 200), StatusCardPreference.GetPosition(PositionPath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("12")]
    [InlineData("a,b")]
    [InlineData("1,2,3")]
    public void AGarbledPositionMeansNone(string content)
    {
        File.WriteAllText(PositionPath, content);
        Assert.Null(StatusCardPreference.GetPosition(PositionPath));
    }

    [Fact]
    public void NoSavedPositionMeansNone() => Assert.Null(StatusCardPreference.GetPosition(PositionPath));

    [Fact]
    public void APositionAlreadyOnScreenIsKept() =>
        Assert.Equal(new Point(500, 300), WaitingCard.KeepOnScreen(new Rectangle(new Point(500, 300), Card), Monitor));

    /// <summary>Saved on a second monitor that has since gone: pulled back to the nearest edge.</summary>
    [Fact]
    public void APositionOffTheRightIsPulledBackFully() =>
        Assert.Equal(new Point(1920 - 354, 300), WaitingCard.KeepOnScreen(new Rectangle(new Point(2500, 300), Card), Monitor));

    [Fact]
    public void APositionOffTheTopLeftIsPulledBackFully() =>
        Assert.Equal(new Point(0, 0), WaitingCard.KeepOnScreen(new Rectangle(new Point(-1500, -40), Card), Monitor));

    /// <summary>Half off the bottom — a taskbar that grew, or a lower resolution.</summary>
    [Fact]
    public void APositionPartlyOffTheBottomIsPulledUp() =>
        Assert.Equal(new Point(100, 1040 - 282), WaitingCard.KeepOnScreen(new Rectangle(new Point(100, 1000), Card), Monitor));

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
