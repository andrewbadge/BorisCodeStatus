namespace BorisCodeStatus.Core.State;

/// <summary>Who sits in the tray. Each is drawn from the same four poses, so the choice is cosmetic.</summary>
public enum Pet
{
    Dog,
    Cat,
    Bot,
    Duck,
    Goat,
}

/// <summary>
/// Which <see cref="Pet"/> the user picked. Tray-only: the pose rule and <c>/status</c> are the same
/// for every pet, so the ESP32 is unaffected. The dog is the default.
/// </summary>
public static class PetPreference
{
    public static Pet Get(string? path = null) =>
        SettingsStore.Load(path).Pet is var pet && Enum.IsDefined(pet) ? pet : Pet.Dog;

    /// <summary>Returns whether it was persisted.</summary>
    public static bool TrySet(Pet pet, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { Pet = pet }, path);
}
