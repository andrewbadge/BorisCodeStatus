using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The pet choice: the dog by default, and any pet by name.</summary>
public class PetPreferenceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public PetPreferenceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void DogByDefault() => Assert.Equal(Pet.Dog, PetPreference.Get(SettingsPath));

    [Theory]
    [InlineData(Pet.Cat)]
    [InlineData(Pet.Bot)]
    [InlineData(Pet.Duck)]
    [InlineData(Pet.Goat)]
    [InlineData(Pet.Dog)]
    public void RemembersTheChoice(Pet pet)
    {
        Assert.True(PetPreference.TrySet(pet, SettingsPath));
        Assert.Equal(pet, PetPreference.Get(SettingsPath));
    }

    [Theory]
    [InlineData("""{ "pet": "Hamster" }""")]
    [InlineData("""{ "pet": 7 }""")]
    [InlineData("""{ "pet": "" }""")]
    [InlineData("{}")]
    public void UnknownContentFallsBackToTheDog(string content)
    {
        File.WriteAllText(SettingsPath, content);
        Assert.Equal(Pet.Dog, PetPreference.Get(SettingsPath));
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
