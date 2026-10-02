using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Tray;

namespace BorisCodeStatus.Core.Tests;

public class TrayIconTests
{
    [Theory]
    [InlineData(DogState.Sleeping)]
    [InlineData(DogState.Idle)]
    [InlineData(DogState.Working)]
    [InlineData(DogState.Waiting)]
    public void TraySpriteIsSquareAndInPalette(DogState state)
    {
        var rows = DogSprites.For(state);

        Assert.Equal(DogSprites.Size, rows.Length);
        Assert.All(rows, row => Assert.Equal(DogSprites.Size, row.Length));
        Assert.All(rows.SelectMany(r => r),
            cell => Assert.InRange(DogSprites.IndexOf(cell), 0, DogSprites.TrayPalette.Length - 1));
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
