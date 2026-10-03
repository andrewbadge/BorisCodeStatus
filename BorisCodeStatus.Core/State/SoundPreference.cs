namespace BorisCodeStatus.Core.State;

/// <summary>The two moments the tray can announce: Claude starts waiting on you, or finishes a turn.</summary>
public enum NotifyMoment
{
    Waiting,
    Idle,
}

/// <summary>
/// Whether a sound plays at a <see cref="NotifyMoment"/>, and which. Independent of the cards:
/// for each moment the user can have the card, the sound, both, or neither.
///
/// Off by default for both moments, so the marker means on: a sound the user did not ask for is
/// the most intrusive thing this app could do. Each pet has its own pair of sounds — the dog pants
/// by default and can woof instead, the cat purrs by default and can meow — chosen separately per
/// moment, and each "instead" is a marker of its own, so switching pets keeps each choice. The
/// bot and the duck follow the same pattern; the names live in <c>WaitingSound.Names</c>.
/// </summary>
public static class SoundPreference
{
    public static bool IsEnabled(NotifyMoment moment, string? path = null) =>
        FlagPreference.IsSet(path ?? EnabledFile(moment));

    public static bool TrySetEnabled(NotifyMoment moment, bool enabled, string? path = null) =>
        FlagPreference.TrySet(path ?? EnabledFile(moment), set: enabled, reason: $"{moment} sound turned on");

    /// <summary>True for the pet's second sound at this moment: the dog's woof, the cat's meow, and so on.</summary>
    public static bool UsesAlternate(NotifyMoment moment, Pet pet, string? path = null) =>
        FlagPreference.IsSet(path ?? AlternateFile(moment, pet));

    public static bool TrySetAlternate(NotifyMoment moment, Pet pet, bool alternate, string? path = null) =>
        FlagPreference.TrySet(path ?? AlternateFile(moment, pet), set: alternate, reason: $"{pet}'s second sound chosen for {moment}");

    private static string EnabledFile(NotifyMoment moment) =>
        moment == NotifyMoment.Idle ? VitalsPaths.IdleSoundFlagFile : VitalsPaths.WaitingSoundFlagFile;

    /// <summary>The dog's and cat's files predate the other pets and keep their names, so upgrades keep the choice.</summary>
    private static string AlternateFile(NotifyMoment moment, Pet pet) => (moment, pet) switch
    {
        (NotifyMoment.Idle, Pet.Cat) => VitalsPaths.IdleCatMeowFlagFile,
        (NotifyMoment.Idle, Pet.Dog) => VitalsPaths.IdleDogWoofFlagFile,
        (_, Pet.Cat) => VitalsPaths.CatMeowFlagFile,
        (_, Pet.Dog) => VitalsPaths.DogWoofFlagFile,
        _ => Path.Combine(VitalsPaths.DataDirectory, $"{(moment == NotifyMoment.Idle ? "idle-" : "")}{pet.ToString().ToLowerInvariant()}-alternate.flag"),
    };
}
