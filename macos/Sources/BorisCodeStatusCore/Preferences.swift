import Foundation

// The thin wrappers callers use instead of `SettingsStore` directly. Each reads the file at the
// moment of use — nothing is cached — and never throws; each setter returns whether the change was
// persisted. `url` is for tests. They mirror the Windows `*Preference` classes one for one, minus
// the HTTP and usage-API ones the Mac build does not have.

/// The two moments the app can announce: Claude starts waiting on you, or finishes a turn.
public enum NotifyMoment: Sendable {
    case waiting
    case idle
}

/// Which `Pet` the user picked. The cat is the Mac default; Windows defaults to the dog.
public enum PetPreference {
    public static func get(url: URL = VitalsPaths.appSettingsFile) -> Pet {
        SettingsStore.load(url: url).pet
    }

    @discardableResult
    public static func trySet(_ pet: Pet, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.pet = pet }
    }
}

/// Whether a card shows when Claude starts waiting (on by default) and when a turn ends (off by
/// default: a turn ends far more often than a prompt appears, so a card for it must be asked for).
public enum NotificationPreference {
    public static func areEnabled(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.load(url: url).notificationsEnabled
    }

    @discardableResult
    public static func trySetEnabled(_ enabled: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.notificationsEnabled = enabled }
    }

    public static func idleCardEnabled(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.load(url: url).idleCardEnabled
    }

    @discardableResult
    public static func trySetIdleCardEnabled(_ enabled: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.idleCardEnabled = enabled }
    }
}

/// Whether a sound plays at a `NotifyMoment`, and which. Independent of the cards: for each moment
/// the user can have the card, the sound, both, or neither. Off by default for both moments. Each
/// pet has a pair of sounds, chosen separately per moment and kept per pet, so switching pets
/// keeps each choice.
public enum SoundPreference {
    public static func isEnabled(_ moment: NotifyMoment, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        let settings = SettingsStore.load(url: url)
        return moment == .idle ? settings.idleSoundEnabled : settings.waitingSoundEnabled
    }

    @discardableResult
    public static func trySetEnabled(_ moment: NotifyMoment, _ enabled: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) {
            if moment == .idle { $0.idleSoundEnabled = enabled } else { $0.waitingSoundEnabled = enabled }
        }
    }

    /// True for the pet's second sound at this moment: the dog's woof, the cat's meow, and so on.
    public static func usesAlternate(_ moment: NotifyMoment, _ pet: Pet, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        let settings = SettingsStore.load(url: url)
        return (moment == .idle ? settings.idleAlternateSoundPets : settings.waitingAlternateSoundPets).contains(pet)
    }

    @discardableResult
    public static func trySetAlternate(_ moment: NotifyMoment, _ pet: Pet, _ alternate: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { settings in
            var pets = (moment == .idle ? settings.idleAlternateSoundPets : settings.waitingAlternateSoundPets).filter { $0 != pet }
            if alternate {
                pets.append(pet)
            }
            if moment == .idle { settings.idleAlternateSoundPets = pets } else { settings.waitingAlternateSoundPets = pets }
        }
    }
}

/// Whether the status card stays on screen, whether it is the mini one, and where the user last
/// dragged it. On the Mac the mini card is kept on screen by default (Windows: both off); the
/// position is absent until the card is first dragged.
public enum StatusCardPreference {
    public static func isPinned(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.load(url: url).statusCardPinned
    }

    @discardableResult
    public static func trySetPinned(_ pinned: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.statusCardPinned = pinned }
    }

    public static func isMini(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.load(url: url).statusCardMini
    }

    @discardableResult
    public static func trySetMini(_ mini: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.statusCardMini = mini }
    }

    /// The last saved top-left corner, in screen points with the origin at the top left of the
    /// main display — the Windows convention, so the stored numbers mean the same thing on both.
    public static func position(url: URL = VitalsPaths.appSettingsFile) -> CardPosition? {
        SettingsStore.load(url: url).statusCardPosition
    }

    @discardableResult
    public static func trySavePosition(_ position: CardPosition, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.statusCardPosition = position }
    }
}

/// Which app, if any, clicking a card brings to the front, stored as its bundle identifier. The
/// default is that a click only dismisses, so a missing or blank value yields nil.
public enum ClickTargetPreference {
    public static func get(url: URL = VitalsPaths.appSettingsFile) -> String? {
        normalise(SettingsStore.load(url: url).clickTargetApp)
    }

    @discardableResult
    public static func trySet(_ bundleIdentifier: String?, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.clickTargetApp = normalise(bundleIdentifier) }
    }

    private static func normalise(_ value: String?) -> String? {
        guard let trimmed = value?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty else { return nil }
        return trimmed
    }
}

/// The 5-hour session usage, in percent, past which the app shows its warning card. 100 is the
/// "never" setting — usage cannot pass it. A missing or out-of-range value yields the default.
public enum SessionWarningPreference {
    public static let defaultPercent = 80
    public static let range = 50...100

    public static func get(url: URL = VitalsPaths.appSettingsFile) -> Int {
        let percent = SettingsStore.load(url: url).sessionWarningPercent
        return range.contains(percent) ? percent : defaultPercent
    }

    @discardableResult
    public static func trySet(_ percent: Int, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.sessionWarningPercent = min(max(percent, range.lowerBound), range.upperBound) }
    }
}

/// Whether the app may ask GitHub whether a newer release exists. Off by default: it is a network
/// call, and the app makes none unless asked to. Also remembers the newest release already
/// announced, so the card appears once per release rather than at every login.
public enum UpdateCheckPreference {
    public static func isEnabled(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.load(url: url).updateCheckEnabled
    }

    @discardableResult
    public static func trySetEnabled(_ enabled: Bool, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.updateCheckEnabled = enabled }
    }

    public static func notifiedVersion(url: URL = VitalsPaths.appSettingsFile) -> String? {
        SettingsStore.load(url: url).updateNotifiedVersion
    }

    @discardableResult
    public static func trySetNotifiedVersion(_ version: String, url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.updateNotifiedVersion = version }
    }
}

/// Whether the app has already turned on Open at Login, once, on the first launch of an installed
/// copy. Whether it is on *now* is not a preference: macOS keeps that (`SMAppService`), and the user
/// can change it in System Settings → General → Login Items without this app knowing. This flag
/// only stops a later launch from turning it back on after the user has turned it off. Windows has
/// no equivalent: its installer always writes a Run key.
public enum LoginItemPreference {
    public static func isSetUp(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.load(url: url).loginItemSetUp
    }

    @discardableResult
    public static func trySetSetUp(url: URL = VitalsPaths.appSettingsFile) -> Bool {
        SettingsStore.tryUpdate(url: url) { $0.loginItemSetUp = true }
    }
}
