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
/// moment, and each "instead" is a marker of its own, so switching pets keeps each choice.
/// </summary>
public static class SoundPreference
{
    public static bool IsEnabled(NotifyMoment moment, string? path = null) =>
        FlagPreference.IsSet(path ?? EnabledFile(moment));

    public static bool TrySetEnabled(NotifyMoment moment, bool enabled, string? path = null) =>
        FlagPreference.TrySet(path ?? EnabledFile(moment), set: enabled, reason: $"{moment} sound turned on");

    /// <summary>True for the pet's second sound at this moment: the dog's woof, or the cat's meow.</summary>
    public static bool UsesAlternate(NotifyMoment moment, bool cat, string? path = null) =>
        FlagPreference.IsSet(path ?? AlternateFile(moment, cat));

    public static bool TrySetAlternate(NotifyMoment moment, bool cat, bool alternate, string? path = null) =>
        FlagPreference.TrySet(path ?? AlternateFile(moment, cat), set: alternate, reason: $"{(cat ? "Meow" : "Woof")} chosen for {moment}");

    private static string EnabledFile(NotifyMoment moment) =>
        moment == NotifyMoment.Idle ? VitalsPaths.IdleSoundFlagFile : VitalsPaths.WaitingSoundFlagFile;

    private static string AlternateFile(NotifyMoment moment, bool cat) => (moment, cat) switch
    {
        (NotifyMoment.Idle, true) => VitalsPaths.IdleCatMeowFlagFile,
        (NotifyMoment.Idle, false) => VitalsPaths.IdleDogWoofFlagFile,
        (_, true) => VitalsPaths.CatMeowFlagFile,
        _ => VitalsPaths.DogWoofFlagFile,
    };
}
