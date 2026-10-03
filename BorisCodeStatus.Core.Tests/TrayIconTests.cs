using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;
using BorisCodeStatus.Tray;

namespace BorisCodeStatus.Core.Tests;

public class TrayIconTests
{
    public static TheoryData<DogState, Pet> EveryPose()
    {
        var data = new TheoryData<DogState, Pet>();
        foreach (var pet in Enum.GetValues<Pet>())
        {
            foreach (var state in Enum.GetValues<DogState>())
            {
                data.Add(state, pet);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryPose))]
    public void TraySpriteIsSquareAndInPalette(DogState state, Pet pet)
    {
        var (rows, palette) = DogSprites.Tray(state, pet);

        Assert.Equal(DogSprites.Size, rows.Length);
        Assert.All(rows, row => Assert.Equal(DogSprites.Size, row.Length));
        Assert.All(rows.SelectMany(r => r), cell => Assert.InRange(DogSprites.IndexOf(cell), 0, palette.Length - 1));
    }

    [Fact]
    public void PortraitsAreRectangularAndInPalette()
    {
        var portraits = new[] { Pet.Cat, Pet.Bot, Pet.Duck }
            .Select(p => DogSprites.Portrait(p)!.Value)
            .Append((DogSprites.CatSleepingPortrait, DogSprites.CatPalette));

        foreach (var (rows, palette) in portraits)
        {
            Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
            Assert.All(rows.SelectMany(r => r), cell => Assert.InRange(DogSprites.IndexOf(cell), 0, palette.Length - 1));
        }
    }

    /// <summary>Every pose must look different, or the pet would hide a state change.</summary>
    [Theory]
    [InlineData(Pet.Dog)]
    [InlineData(Pet.Cat)]
    [InlineData(Pet.Bot)]
    [InlineData(Pet.Duck)]
    public void EachPoseIsDistinct(Pet pet)
    {
        var grids = Enum.GetValues<DogState>().Select(s => string.Concat(DogSprites.Tray(s, pet).Grid));

        Assert.Equal(4, grids.Distinct().Count());
    }

    /// <summary>A missing recording plays silence rather than failing, so only this would notice.</summary>
    [Theory]
    [InlineData(Pet.Dog)]
    [InlineData(Pet.Cat)]
    [InlineData(Pet.Bot)]
    [InlineData(Pet.Duck)]
    public void EverySoundIsEmbedded(Pet pet)
    {
        var embedded = typeof(TrayIconRenderer).Assembly.GetManifestResourceNames();

        Assert.Contains(WaitingSound.ResourceName(pet, alternate: false), embedded);
        Assert.Contains(WaitingSound.ResourceName(pet, alternate: true), embedded);
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
