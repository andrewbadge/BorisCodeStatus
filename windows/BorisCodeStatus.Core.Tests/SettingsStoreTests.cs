using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>The settings file: defaults without a write, safe on bad input, and a one-time import of the old files.</summary>
public class SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisCodeStatusTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_directory);

    private void Legacy(string name, string content = "") => File.WriteAllText(Path.Combine(_directory, name), content);

    [Fact]
    public void DefaultsWithNothingOnDiskAndNoFirstRunWrite()
    {
        var settings = SettingsStore.Load(SettingsPath);

        Assert.False(settings.HttpEnabled);
        Assert.False(settings.UsageApiEnabled);
        Assert.True(settings.NotificationsEnabled);
        Assert.False(settings.IdleCardEnabled);
        Assert.False(settings.WaitingSoundEnabled);
        Assert.False(settings.IdleSoundEnabled);
        Assert.Equal(Pet.Dog, settings.Pet);
        Assert.Null(settings.ClickTargetApp);
        Assert.Equal(80, settings.SessionWarningPercent);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void ChangesSurviveAReload()
    {
        Assert.True(SettingsStore.TryUpdate(s => s with { HttpEnabled = true, Pet = Pet.Goat }, SettingsPath));

        var settings = SettingsStore.Load(SettingsPath);
        Assert.True(settings.HttpEnabled);
        Assert.Equal(Pet.Goat, settings.Pet);
    }

    [Fact]
    public void WritesReadableSnakeCaseJsonWithPetsByName()
    {
        SettingsStore.TryUpdate(s => s with { Pet = Pet.Cat, WaitingAlternateSoundPets = [Pet.Cat] }, SettingsPath);

        var json = File.ReadAllText(SettingsPath);
        Assert.Contains("\"pet\": \"Cat\"", json);
        Assert.Contains("\"http_enabled\": false", json);
        Assert.Contains("\"waiting_alternate_sound_pets\": [", json);
    }

    [Fact]
    public void AMemberMissingFromTheFileTakesItsDefault()
    {
        File.WriteAllText(SettingsPath, """{ "pet": "Duck" }""");

        var settings = SettingsStore.Load(SettingsPath);

        Assert.Equal(Pet.Duck, settings.Pet);
        Assert.True(settings.NotificationsEnabled);
        Assert.False(settings.HttpEnabled);
    }

    /// <summary>A hand edit with a typo must fail safe, and must not be destroyed by the next toggle.</summary>
    [Fact]
    public void AGarbledFileYieldsDefaultsAndIsBackedUpBeforeBeingReplaced()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        Assert.False(SettingsStore.Load(SettingsPath).HttpEnabled);
        Assert.True(SettingsStore.TryUpdate(s => s with { HttpEnabled = true }, SettingsPath));

        Assert.True(SettingsStore.Load(SettingsPath).HttpEnabled);
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath + ".bak"));
    }

    [Fact]
    public void ReportsFailureRatherThanThrowingWhenItCannotWrite()
    {
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, "not a directory");

        Assert.False(SettingsStore.TryUpdate(s => s with { HttpEnabled = true }, Path.Combine(blocker, "settings.json")));
        Assert.False(SettingsStore.Load(Path.Combine(blocker, "settings.json")).HttpEnabled);
    }

    [Fact]
    public void NoTemporaryFileIsLeftBehind()
    {
        SettingsStore.TryUpdate(s => s with { HttpEnabled = true }, SettingsPath);

        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    [Fact]
    public void ImportsTheOldFilesOnceAndRemovesThem()
    {
        Legacy("http-enabled.flag");
        Legacy("notifications-disabled.flag");
        Legacy("idle-card.flag");
        Legacy("idle-sound.flag");
        Legacy("pet.txt", "Goat");
        Legacy("goat-alternate.flag");
        Legacy("idle-cat-meow.flag");
        Legacy("card-click-app.txt", "WindowsTerminal");
        Legacy("status-card-pinned.flag");
        Legacy("status-card-position.txt", "-1500,200");
        Legacy("session-warning.txt", "65");

        var settings = SettingsStore.Load(SettingsPath);

        Assert.True(settings.HttpEnabled);
        Assert.False(settings.NotificationsEnabled);
        Assert.True(settings.IdleCardEnabled);
        Assert.True(settings.IdleSoundEnabled);
        Assert.False(settings.WaitingSoundEnabled);
        Assert.Equal(Pet.Goat, settings.Pet);
        Assert.Equal([Pet.Goat], settings.WaitingAlternateSoundPets);
        Assert.Equal([Pet.Cat], settings.IdleAlternateSoundPets);
        Assert.Equal("WindowsTerminal", settings.ClickTargetApp);
        Assert.True(settings.StatusCardPinned);
        Assert.Equal(new CardPosition(-1500, 200), settings.StatusCardPosition);
        Assert.Equal(65, settings.SessionWarningPercent);

        Assert.True(File.Exists(SettingsPath));
        Assert.Equal(["settings.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));

        // A second read comes from the new file, with the same answers.
        Assert.Equal(settings.Pet, SettingsStore.Load(SettingsPath).Pet);
    }

    [Fact]
    public void TheOldCatMarkerIsHonouredWhenThereIsNoPetFile()
    {
        Legacy("cat-person.flag");

        Assert.Equal(Pet.Cat, SettingsStore.Load(SettingsPath).Pet);
    }

    /// <summary>Earlier versions defaulted the service on and recorded a pause; that file means nothing now.</summary>
    [Fact]
    public void TheOldPausedMarkerDoesNotEnableHttpAndIsCleanedUp()
    {
        Legacy("http-paused.flag");

        Assert.False(SettingsStore.Load(SettingsPath).HttpEnabled);
        Assert.False(File.Exists(Path.Combine(_directory, "http-paused.flag")));
    }

    /// <summary>If the new file cannot be written the old files stay, so the next read retries rather than losing the choices.</summary>
    [Fact]
    public void OldFilesStayWhenTheImportCannotBeSaved()
    {
        Legacy("http-enabled.flag");
        Directory.CreateDirectory(SettingsPath + ".tmp"); // Where the write goes first, so it fails.

        Assert.True(SettingsStore.Load(SettingsPath).HttpEnabled);
        Assert.True(File.Exists(Path.Combine(_directory, "http-enabled.flag")));
    }

    [Fact]
    public void AnExistingSettingsFileIsNotOverwrittenByStrayOldFiles()
    {
        File.WriteAllText(SettingsPath, """{ "pet": "Duck" }""");
        Legacy("http-enabled.flag");

        var settings = SettingsStore.Load(SettingsPath);

        Assert.Equal(Pet.Duck, settings.Pet);
        Assert.False(settings.HttpEnabled);
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
