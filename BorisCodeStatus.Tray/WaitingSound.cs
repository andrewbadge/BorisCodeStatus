using System.Media;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Plays the pet's sound for "Claude is waiting". The four recordings are embedded in this
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

    /// <summary>Panting or woof for the dog, purr or meow for the cat. Returns immediately.</summary>
    public static void Play(bool cat, bool alternate)
    {
        var name = (cat, alternate) switch
        {
            (false, false) => "DogPanting",
            (false, true) => "DogWoof",
            (true, false) => "CatPurr",
            (true, true) => "CatMeow",
        };

        var stream = typeof(WaitingSound).Assembly.GetManifestResourceStream($"Sounds.{name}.wav");
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
