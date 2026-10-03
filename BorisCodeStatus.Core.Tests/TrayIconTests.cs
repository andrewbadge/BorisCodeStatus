using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Tray;

namespace BorisCodeStatus.Core.Tests;

public class TrayIconTests
{
    [Theory]
    [InlineData(DogState.Sleeping, false)]
    [InlineData(DogState.Idle, false)]
    [InlineData(DogState.Working, false)]
    [InlineData(DogState.Waiting, false)]
    [InlineData(DogState.Sleeping, true)]
    [InlineData(DogState.Idle, true)]
    [InlineData(DogState.Working, true)]
    [InlineData(DogState.Waiting, true)]
    public void TraySpriteIsSquareAndInPalette(DogState state, bool cat)
    {
        var (rows, palette) = DogSprites.Tray(state, cat);

        Assert.Equal(DogSprites.Size, rows.Length);
        Assert.All(rows, row => Assert.Equal(DogSprites.Size, row.Length));
        Assert.All(rows.SelectMany(r => r), cell => Assert.InRange(DogSprites.IndexOf(cell), 0, palette.Length - 1));
    }

    [Fact]
    public void CatPortraitsAreRectangularAndInPalette()
    {
        foreach (var rows in new[] { DogSprites.CatPortrait, DogSprites.CatSleepingPortrait })
        {
            Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
            Assert.All(rows.SelectMany(r => r), cell => Assert.InRange(DogSprites.IndexOf(cell), 0, DogSprites.CatPalette.Length - 1));
        }
    }

    /// <summary>Every pose must look different, or the cat would hide a state change.</summary>
    [Fact]
    public void EachCatPoseIsDistinct()
    {
        var grids = new[] { DogState.Sleeping, DogState.Idle, DogState.Working, DogState.Waiting }
            .Select(s => string.Concat(DogSprites.Tray(s, cat: true).Grid));

        Assert.Equal(4, grids.Distinct().Count());
    }

    [Theory]
    [InlineData(0, 49.9, 75.1)]
    public void QuotaRingIsGreenAmberRedAtTheBandEdges(double low, double belowHalf, double aboveThreeQuarters)
    {
        var green = TrayIconRenderer.QuotaColorFor(low);
        Assert.Equal(green, TrayIconRenderer.QuotaColorFor(belowHalf));

        var amber = TrayIconRenderer.QuotaColorFor(50);
        Assert.NotEqual(green, amber);
        Assert.Equal(amber, TrayIconRenderer.QuotaColorFor(75));

        var red = TrayIconRenderer.QuotaColorFor(aboveThreeQuarters);
        Assert.NotEqual(amber, red);
        Assert.NotEqual(green, red);
    }
}
