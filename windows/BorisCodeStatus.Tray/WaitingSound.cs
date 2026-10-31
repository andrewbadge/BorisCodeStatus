using System.Media;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Plays the pet's sound for "Claude is waiting" or a finished turn. The recordings are embedded in this
/// assembly (see the .csproj), so the installer needs no extra files and a sound cannot go
/// missing from the install folder.
///
/// <see cref="SoundPlayer"/> because it is in the box and plays exactly what these are —
/// 16-bit PCM WAV — with no new dependency; it cannot play MP3 or M4A, which is why they are WAV.
/// </summary>
internal static class WaitingSound
{
    /// <summary>
    /// The one player. Kept in a field so it is not collected mid-sound, and replaced on each
    /// play, which also stops a sound still playing rather than talking over it.
    /// </summary>
    private static SoundPlayer? _player;

    /// <summary>
    /// Each pet's default and second sound, as shown in Settings. The recording is
    /// <c>Sounds\{Pet}{Name}.wav</c> with the spaces dropped — e.g. <c>DuckFlyaway.wav</c> — and
    /// is embedded by the .csproj's glob.
    /// </summary>
    public static (string Default, string Alternate) Names(Pet pet) => pet switch
    {
        Pet.Cat => ("Purr", "Meow"),
        Pet.Bot => ("Chirp", "Clamp"),
        Pet.Duck => ("Quack", "Fly away"),
        Pet.Goat => ("Bleat", "Herd"),
        _ => ("Panting", "Woof"),
    };

    internal static string ResourceName(Pet pet, bool alternate)
    {
        var names = Names(pet);
        return $"Sounds.{pet}{(alternate ? names.Alternate : names.Default).Replace(" ", "", StringComparison.Ordinal)}.wav";
    }

    /// <summary>The pet's sound for the moment. Returns immediately; silent if the recording is missing from the build.</summary>
    public static void Play(Pet pet, bool alternate)
    {
        var stream = typeof(WaitingSound).Assembly.GetManifestResourceStream(ResourceName(pet, alternate));
        if (stream is null)
        {
            return;
        }

        _player?.Stop();
        _player?.Dispose();

        // Load copies the whole stream into memory, so the stream can go before the sound ends.
        _player = new SoundPlayer(stream);
        _player.Load();
        stream.Dispose();
        _player.Play();
    }
}
