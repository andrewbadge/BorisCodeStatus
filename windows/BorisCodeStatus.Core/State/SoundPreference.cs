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
/// Off by default for both moments: a sound the user did not ask for is the most intrusive thing
/// this app could do. Each pet has its own pair of sounds — the dog pants by default and can woof
/// instead, the cat purrs by default and can meow — chosen separately per moment and kept per
/// pet, so switching pets keeps each choice. The bot and the duck follow the same pattern; the
/// names live in <c>WaitingSound.Names</c>.
/// </summary>
public static class SoundPreference
{
    public static bool IsEnabled(NotifyMoment moment, string? path = null)
    {
        var settings = SettingsStore.Load(path);
        return moment == NotifyMoment.Idle ? settings.IdleSoundEnabled : settings.WaitingSoundEnabled;
    }

    public static bool TrySetEnabled(NotifyMoment moment, bool enabled, string? path = null) =>
        SettingsStore.TryUpdate(
            s => moment == NotifyMoment.Idle ? s with { IdleSoundEnabled = enabled } : s with { WaitingSoundEnabled = enabled },
            path);

    /// <summary>True for the pet's second sound at this moment: the dog's woof, the cat's meow, and so on.</summary>
    public static bool UsesAlternate(NotifyMoment moment, Pet pet, string? path = null) =>
        AlternatePets(SettingsStore.Load(path), moment).Contains(pet);

    public static bool TrySetAlternate(NotifyMoment moment, Pet pet, bool alternate, string? path = null) =>
        SettingsStore.TryUpdate(
            s =>
            {
                var pets = AlternatePets(s, moment).Where(p => p != pet).ToList();
                if (alternate)
                {
                    pets.Add(pet);
                }

                return moment == NotifyMoment.Idle ? s with { IdleAlternateSoundPets = pets } : s with { WaitingAlternateSoundPets = pets };
            },
            path);

    private static IReadOnlyList<Pet> AlternatePets(AppSettings settings, NotifyMoment moment) =>
        moment == NotifyMoment.Idle ? settings.IdleAlternateSoundPets : settings.WaitingAlternateSoundPets;
}
