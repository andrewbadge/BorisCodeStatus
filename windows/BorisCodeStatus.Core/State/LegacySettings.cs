using System.Globalization;

namespace BorisCodeStatus.Core.State;

/// <summary>
/// One-time import of the preferences that used to be a scatter of marker and text files, so an
/// upgrade keeps everyone's choices. Runs when <c>settings.json</c> does not exist and finds at
/// least one of the old files beside it; the files are deleted only once the new one is safely
/// written, so a failed write retries on the next read instead of losing the choices.
///
/// The old convention was that a file marked the <em>non-default</em> setting, which is why some
/// flags below mean "off" and the rest "on".
/// </summary>
internal static class LegacySettings
{
    private static readonly string[] Files =
    [
        "http-enabled.flag", "http-paused.flag", "usage-api-enabled.flag", "notifications-disabled.flag",
        "idle-card.flag", "waiting-sound.flag", "idle-sound.flag", "pet.txt", "cat-person.flag",
        "card-click-app.txt", "status-card-pinned.flag", "status-card-mini.flag", "status-card-position.txt",
        "session-warning.txt",
        .. AlternateFiles(),
    ];

    /// <summary>The settings read from the old files next to <paramref name="settingsPath"/>, or null if there are none.</summary>
    public static AppSettings? Migrate(string settingsPath, Func<string, AppSettings, bool> write)
    {
        var directory = Path.GetDirectoryName(settingsPath)!;
        if (!Directory.Exists(directory) || !Files.Any(name => File.Exists(Path.Combine(directory, name))))
        {
            return null;
        }

        bool Has(string name) => File.Exists(Path.Combine(directory, name));

        string? Text(string name)
        {
            try
            {
                var text = File.ReadAllText(Path.Combine(directory, name)).Trim();
                return text.Length == 0 ? null : text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        var pet = Enum.TryParse<Pet>(Text("pet.txt"), ignoreCase: true, out var named) && Enum.IsDefined(named)
            ? named
            : Has("cat-person.flag") ? Pet.Cat : Pet.Dog;

        CardPosition? position = null;
        if (Text("status-card-position.txt")?.Split(',') is [var x, var y]
            && int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var left)
            && int.TryParse(y, NumberStyles.Integer, CultureInfo.InvariantCulture, out var top))
        {
            position = new CardPosition(left, top);
        }

        var warning = int.TryParse(Text("session-warning.txt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            && percent is >= SessionWarningPreference.Minimum and <= SessionWarningPreference.Maximum
                ? percent
                : SessionWarningPreference.Default;

        var settings = new AppSettings
        {
            HttpEnabled = Has("http-enabled.flag"),
            UsageApiEnabled = Has("usage-api-enabled.flag"),
            NotificationsEnabled = !Has("notifications-disabled.flag"),
            IdleCardEnabled = Has("idle-card.flag"),
            WaitingSoundEnabled = Has("waiting-sound.flag"),
            IdleSoundEnabled = Has("idle-sound.flag"),
            Pet = pet,
            WaitingAlternateSoundPets = [.. Enum.GetValues<Pet>().Where(p => Has(AlternateFile(false, p)))],
            IdleAlternateSoundPets = [.. Enum.GetValues<Pet>().Where(p => Has(AlternateFile(true, p)))],
            ClickTargetApp = Text("card-click-app.txt"),
            StatusCardPinned = Has("status-card-pinned.flag"),
            StatusCardMini = Has("status-card-mini.flag"),
            StatusCardPosition = position,
            SessionWarningPercent = warning,
        };

        if (write(settingsPath, settings))
        {
            foreach (var name in Files)
            {
                try
                {
                    File.Delete(Path.Combine(directory, name));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Already imported; a stray file is ignored from here on.
                }
            }
        }

        return settings;
    }

    private static IEnumerable<string> AlternateFiles() =>
        Enum.GetValues<Pet>().SelectMany(pet => new[] { AlternateFile(false, pet), AlternateFile(true, pet) });

    /// <summary>The dog's and cat's files predate the other pets and kept their own names.</summary>
    private static string AlternateFile(bool idle, Pet pet) => (idle, pet) switch
    {
        (true, Pet.Cat) => "idle-cat-meow.flag",
        (true, Pet.Dog) => "idle-dog-woof.flag",
        (false, Pet.Cat) => "cat-meow.flag",
        (false, Pet.Dog) => "dog-woof.flag",
        _ => $"{(idle ? "idle-" : "")}{pet.ToString().ToLowerInvariant()}-alternate.flag",
    };
}
