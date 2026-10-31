import AppKit
import BorisCodeStatusCore

/// Plays the pet's sound for "Claude is waiting" or a finished turn, from the same WAV recordings
/// the Windows build embeds. `scripts/build-app.sh` copies them from `BorisCodeStatus.Tray/Sounds`
/// into `Contents/Resources/Sounds`, so there is one copy of each in the repo; under `swift run`
/// there is no bundle and every sound is silently missing, as Windows is when one is.
@MainActor
public enum WaitingSound {
    /// The one sound playing. Kept so it is not released mid-play, and stopped before the next, so
    /// two never talk over each other.
    private static var current: NSSound?

    /// Each pet's default and second sound, as shown in Settings. The recording is
    /// `Sounds/{Pet}{Name}.wav` with the spaces dropped — e.g. `DuckFlyaway.wav`.
    public nonisolated static func names(_ pet: Pet) -> (default: String, alternate: String) {
        switch pet {
        case .cat: ("Purr", "Meow")
        case .bot: ("Chirp", "Clamp")
        case .duck: ("Quack", "Fly away")
        case .goat: ("Bleat", "Herd")
        case .dog: ("Panting", "Woof")
        }
    }

    public nonisolated static func fileName(_ pet: Pet, alternate: Bool) -> String {
        let names = names(pet)
        return pet.rawValue + (alternate ? names.alternate : names.default).replacingOccurrences(of: " ", with: "") + ".wav"
    }

    /// The pet's sound for the moment. Returns immediately; silent if the recording is missing.
    public static func play(_ pet: Pet, alternate: Bool) {
        guard let url = Bundle.main.resourceURL?.appending(path: "Sounds").appending(path: fileName(pet, alternate: alternate)),
              let sound = NSSound(contentsOf: url, byReference: true)
        else { return }

        current?.stop()
        current = sound
        sound.play()
    }
}
