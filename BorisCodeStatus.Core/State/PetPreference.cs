namespace BorisCodeStatus.Core.State;

/// <summary>
/// "Are you a dog or a cat person?" The dog is the default, so the marker file means cat.
/// Tray-only: the pose rule and <c>/status</c> are the same for both, so the ESP32 is unaffected.
/// </summary>
public static class PetPreference
{
    public static bool IsCat(string? path = null) =>
        FlagPreference.IsSet(path ?? VitalsPaths.CatPersonFlagFile);

    public static bool TrySetCat(bool cat, string? path = null) =>
        FlagPreference.TrySet(path ?? VitalsPaths.CatPersonFlagFile, set: cat, reason: "Cat chosen");
}
