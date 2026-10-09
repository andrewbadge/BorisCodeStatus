using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>
/// The notification sounds and the idle card: all off by default, and each moment and each pet
/// keeps its own choice of sound.
/// </summary>
public class SoundPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    public SoundPreferenceTests() => Directory.CreateDirectory(_directory);

    private string Flag(string name) => Path.Combine(_directory, name);

    [Theory]
    [InlineData(NotifyMoment.Waiting)]
    [InlineData(NotifyMoment.Idle)]
    public void SoundIsOffByDefault(NotifyMoment moment) =>
        Assert.False(SoundPreference.IsEnabled(moment, Flag($"{moment}-sound.flag")));

    [Fact]
    public void RemembersBeingSwitchedOn()
    {
        Assert.True(SoundPreference.TrySetEnabled(NotifyMoment.Idle, true, Flag("idle-sound.flag")));
        Assert.True(SoundPreference.IsEnabled(NotifyMoment.Idle, Flag("idle-sound.flag")));
    }

    /// <summary>Panting for the dog, purr for the cat, until the user picks otherwise.</summary>
    [Theory]
    [InlineData(NotifyMoment.Waiting, Pet.Dog)]
    [InlineData(NotifyMoment.Waiting, Pet.Cat)]
    [InlineData(NotifyMoment.Idle, Pet.Bot)]
    [InlineData(NotifyMoment.Idle, Pet.Duck)]
    [InlineData(NotifyMoment.Waiting, Pet.Goat)]
    public void EachPetStartsOnItsDefaultSound(NotifyMoment moment, Pet pet) =>
        Assert.False(SoundPreference.UsesAlternate(moment, pet, Flag($"{moment}-{pet}.flag")));

    [Fact]
    public void ThePetsKeepSeparateChoices()
    {
        SoundPreference.TrySetAlternate(NotifyMoment.Waiting, Pet.Dog, alternate: true, Flag("dog-woof.flag"));

        Assert.True(SoundPreference.UsesAlternate(NotifyMoment.Waiting, Pet.Dog, Flag("dog-woof.flag")));
        Assert.False(SoundPreference.UsesAlternate(NotifyMoment.Waiting, Pet.Cat, Flag("cat-meow.flag")));
    }

    /// <summary>A turn ends far more often than a prompt appears, so its card must be asked for.</summary>
    [Fact]
    public void IdleCardIsOffByDefaultAndRemembered()
    {
        Assert.False(NotificationPreference.IdleCardEnabled(Flag("idle-card.flag")));

        NotificationPreference.TrySetIdleCardEnabled(true, Flag("idle-card.flag"));
        Assert.True(NotificationPreference.IdleCardEnabled(Flag("idle-card.flag")));
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
