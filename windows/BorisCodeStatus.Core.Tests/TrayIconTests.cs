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
            .Concat(Enumerable.Range(0, DogSprites.WagFrames).Select(f => DogSprites.Portrait(Pet.Goat, f)!.Value))
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
    [InlineData(Pet.Goat)]
    public void EachPoseIsDistinct(Pet pet)
    {
        var grids = Enum.GetValues<DogState>().Select(s => string.Concat(DogSprites.Tray(s, pet).Grid));

        Assert.Equal(4, grids.Distinct().Count());
    }

    /// <summary>
    /// The goat's tongue wags while working or waiting, so the two frames of a busy pose must
    /// differ — and differ only in the tongue and beard, never the badge that carries the state.
    /// </summary>
    [Theory]
    [InlineData(DogState.Working)]
    [InlineData(DogState.Waiting)]
    public void GoatTongueWagsWhileBusy(DogState state)
    {
        var (left, _) = DogSprites.Tray(state, Pet.Goat, frame: 0);
        var (right, palette) = DogSprites.Tray(state, Pet.Goat, frame: 1);

        Assert.NotEqual(string.Concat(left), string.Concat(right));

        // Whatever changed between frames must be tongue (8), beard fur (2) or outline (1), mouth
        // (7) or empty (0): the horns, eyes and badge colours must not move.
        var moved = Enumerable.Range(0, DogSprites.Size)
            .SelectMany(y => Enumerable.Range(0, DogSprites.Size).Select(x => (y, x)))
            .Where(p => left[p.y][p.x] != right[p.y][p.x])
            .SelectMany(p => new[] { left[p.y][p.x], right[p.y][p.x] })
            .Distinct();
        Assert.All(moved, cell => Assert.Contains(cell, "01278"));
        Assert.Equal(DogSprites.GoatPalette, palette);
    }

    /// <summary>Idle and asleep the goat is still — and idle, its mouth is shut: no tongue (8) or open mouth (7).</summary>
    [Theory]
    [InlineData(DogState.Idle)]
    [InlineData(DogState.Sleeping)]
    public void QuietGoatHoldsStill(DogState state)
    {
        Assert.False(DogSprites.Wags(Pet.Goat, state));
        Assert.Equal(
            DogSprites.Tray(state, Pet.Goat, frame: 0).Grid,
            DogSprites.Tray(state, Pet.Goat, frame: 1).Grid);
    }

    [Fact]
    public void IdleGoatHasItsMouthShut() =>
        Assert.DoesNotContain(DogSprites.Tray(DogState.Idle, Pet.Goat).Grid, row => row.Contains('7') || row.Contains('8'));

    [Fact]
    public void OtherPetsIgnoreTheWagFrame()
    {
        foreach (var pet in Enum.GetValues<Pet>().Where(p => p != Pet.Goat))
        {
            foreach (var state in Enum.GetValues<DogState>())
            {
                Assert.Equal(DogSprites.Tray(state, pet, 0).Grid, DogSprites.Tray(state, pet, 1).Grid);
            }
        }
    }

    [Fact]
    public void WagFrameAlternatesEveryInterval()
    {
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var first = DogSprites.WagFrame(start);

        Assert.Equal(first, DogSprites.WagFrame(start + (DogSprites.WagInterval / 2)));
        Assert.NotEqual(first, DogSprites.WagFrame(start + DogSprites.WagInterval));
        Assert.Equal(first, DogSprites.WagFrame(start + (DogSprites.WagInterval * DogSprites.WagFrames)));
    }

    /// <summary>A missing recording plays silence rather than failing, so only this would notice.</summary>
    [Theory]
    [InlineData(Pet.Dog)]
    [InlineData(Pet.Cat)]
    [InlineData(Pet.Bot)]
    [InlineData(Pet.Duck)]
    [InlineData(Pet.Goat)]
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
