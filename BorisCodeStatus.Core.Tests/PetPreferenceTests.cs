using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The pet choice: the dog by default, any pet by name, and the old cat marker still honoured.</summary>
public class PetPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string PetFile => Path.Combine(_directory, "pet.txt");

    private string CatFlag => Path.Combine(_directory, "cat-person.flag");

    public PetPreferenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void DogByDefault() => Assert.Equal(Pet.Dog, PetPreference.Get(PetFile, CatFlag));

    [Theory]
    [InlineData(Pet.Cat)]
    [InlineData(Pet.Bot)]
    [InlineData(Pet.Duck)]
    [InlineData(Pet.Goat)]
    [InlineData(Pet.Dog)]
    public void RemembersTheChoice(Pet pet)
    {
        Assert.True(PetPreference.TrySet(pet, PetFile, CatFlag));
        Assert.Equal(pet, PetPreference.Get(PetFile, CatFlag));
    }

    /// <summary>An upgrade from the two-pet version keeps the cat, and the next choice retires the marker.</summary>
    [Fact]
    public void OldCatMarkerIsHonouredUntilTheNextChoice()
    {
        File.WriteAllText(CatFlag, "");
        Assert.Equal(Pet.Cat, PetPreference.Get(PetFile, CatFlag));

        PetPreference.TrySet(Pet.Dog, PetFile, CatFlag);
        Assert.Equal(Pet.Dog, PetPreference.Get(PetFile, CatFlag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Hamster")]
    [InlineData("7")]
    public void UnknownContentFallsBackToTheDog(string content)
    {
        File.WriteAllText(PetFile, content);
        Assert.Equal(Pet.Dog, PetPreference.Get(PetFile, CatFlag));
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
