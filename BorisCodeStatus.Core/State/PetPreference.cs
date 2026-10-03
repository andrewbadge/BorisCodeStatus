namespace BorisCodeStatus.Core.State;

/// <summary>Who sits in the tray. Each is drawn from the same four poses, so the choice is cosmetic.</summary>
public enum Pet
{
    Dog,
    Cat,
    Bot,
    Duck,
}

/// <summary>
/// Which <see cref="Pet"/> the user picked. Tray-only: the pose rule and <c>/status</c> are the same
/// for every pet, so the ESP32 is unaffected.
///
/// Stored by name in <c>pet.txt</c>, like <see cref="ClickTargetPreference"/>; the dog is the
/// default, so it is the file's absence. Before there were four pets the cat was a marker file,
/// <c>cat-person.flag</c>, and that is still honoured when <c>pet.txt</c> is missing so an upgrade
/// keeps the cat. Any choice made since removes it.
/// </summary>
public static class PetPreference
{
    public static Pet Get(string? path = null, string? legacyCatPath = null)
    {
        try
        {
            if (Enum.TryParse<Pet>(File.ReadAllText(path ?? VitalsPaths.PetFile).Trim(), ignoreCase: true, out var pet)
                && Enum.IsDefined(pet))
            {
                return pet;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Includes FileNotFound: fall through to the legacy marker, then the default.
        }

        return FlagPreference.IsSet(legacyCatPath ?? VitalsPaths.CatPersonFlagFile) ? Pet.Cat : Pet.Dog;
    }

    /// <summary>Never throws, for the same reason as <see cref="FlagPreference.TrySet"/>. Returns whether it was persisted.</summary>
    public static bool TrySet(Pet pet, string? path = null, string? legacyCatPath = null)
    {
        path ??= VitalsPaths.PetFile;
        try
        {
            if (pet == Pet.Dog)
            {
                File.Delete(path);
            }
            else
            {
                VitalsPaths.EnsureDataDirectory();
                File.WriteAllText(path, pet.ToString());
            }

            File.Delete(legacyCatPath ?? VitalsPaths.CatPersonFlagFile);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
