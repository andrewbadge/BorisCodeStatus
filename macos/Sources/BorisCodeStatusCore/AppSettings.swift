import Foundation

/// A top-left screen position, as the status card was last left.
public struct CardPosition: Codable, Equatable, Sendable {
    public var x: Int
    public var y: Int

    public init(x: Int, y: Int) {
        self.x = x
        self.y = y
    }
}

/// Every app preference, as the contents of `settings.json`. A property's initial value is its
/// default, so a missing or unreadable file — and a member missing from one — yields the default
/// and nothing is written until the user changes something.
///
/// Not `state.json`: that is the payload the hook process rewrites constantly. The member names
/// are the Windows build's, so the two files read alike, but `http_enabled` and
/// `usage_api_enabled` do not exist here because the Mac build has neither (see
/// docs/macos-port.md). A file that carries them anyway — copied from Windows, say — loses them on
/// the next write, which is harmless.
///
/// Sounds and the idle card are off by default because they are the most intrusive things the app
/// does; the update check is off because it is the only network call.
///
/// Access goes through `SettingsStore`, usually via the `*Preference` types.
public struct AppSettings: Codable, Equatable, Sendable {
    /// The card shown when Claude starts waiting. On by default, unlike the idle card.
    public var notificationsEnabled = true
    public var idleCardEnabled = false
    public var waitingSoundEnabled = false
    public var idleSoundEnabled = false

    /// The cat on the Mac, the dog on Windows: a deliberate difference between the two builds.
    public var pet: Pet = .cat

    /// The pets whose second sound (woof, meow, …) is chosen for each moment. Kept per pet so
    /// switching pets does not lose a choice.
    public var waitingAlternateSoundPets: [Pet] = []
    public var idleAlternateSoundPets: [Pet] = []

    /// The bundle identifier a click on a card brings forward; nil means a click only dismisses.
    /// Windows stores a process name under the same key; neither build can use the other's value.
    public var clickTargetApp: String?

    /// On by default on the Mac, so the mini card is on screen from the first launch; off on
    /// Windows. A deliberate difference between the two builds.
    public var statusCardPinned = true
    public var statusCardMini = true

    /// Stored as it was left and only kept on screen when it is used, because the displays
    /// present at that point are the ones that matter.
    public var statusCardPosition: CardPosition?

    /// Whether the app may ask GitHub for the latest release. See `UpdateCheckPreference`.
    public var updateCheckEnabled = false

    /// The newest release already announced, so a restart does not announce it again.
    public var updateNotifiedVersion: String?

    /// The 5-hour usage, in percent, past which the warning card shows. See `SessionWarningPreference`.
    public var sessionWarningPercent = SessionWarningPreference.defaultPercent

    /// Mac only. Whether the first launch of an installed copy has already turned on Open at
    /// Login. See `LoginItemPreference`.
    public var loginItemSetUp = false

    public init() {}

    enum CodingKeys: String, CodingKey {
        case notificationsEnabled = "notifications_enabled"
        case idleCardEnabled = "idle_card_enabled"
        case waitingSoundEnabled = "waiting_sound_enabled"
        case idleSoundEnabled = "idle_sound_enabled"
        case pet
        case waitingAlternateSoundPets = "waiting_alternate_sound_pets"
        case idleAlternateSoundPets = "idle_alternate_sound_pets"
        case clickTargetApp = "click_target_app"
        case statusCardPinned = "status_card_pinned"
        case statusCardMini = "status_card_mini"
        case statusCardPosition = "status_card_position"
        case updateCheckEnabled = "update_check_enabled"
        case updateNotifiedVersion = "update_notified_version"
        case sessionWarningPercent = "session_warning_percent"
        case loginItemSetUp = "login_item_set_up"
    }

    /// Member by member, and forgiving of each: a missing member or one of the wrong type keeps
    /// its default rather than costing the whole file. That is stricter than nothing and looser
    /// than Windows, where one bad member resets everything — a hand edit here loses only itself.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        func value<T: Decodable>(_ key: CodingKeys, _ fallback: T) -> T {
            (try? c.decodeIfPresent(T.self, forKey: key)) ?? fallback
        }

        notificationsEnabled = value(.notificationsEnabled, notificationsEnabled)
        idleCardEnabled = value(.idleCardEnabled, idleCardEnabled)
        waitingSoundEnabled = value(.waitingSoundEnabled, waitingSoundEnabled)
        idleSoundEnabled = value(.idleSoundEnabled, idleSoundEnabled)
        pet = value(.pet, pet)
        waitingAlternateSoundPets = Self.pets(c, .waitingAlternateSoundPets)
        idleAlternateSoundPets = Self.pets(c, .idleAlternateSoundPets)
        clickTargetApp = value(.clickTargetApp, clickTargetApp)
        statusCardPinned = value(.statusCardPinned, statusCardPinned)
        statusCardMini = value(.statusCardMini, statusCardMini)
        statusCardPosition = value(.statusCardPosition, statusCardPosition)
        updateCheckEnabled = value(.updateCheckEnabled, updateCheckEnabled)
        updateNotifiedVersion = value(.updateNotifiedVersion, updateNotifiedVersion)
        sessionWarningPercent = value(.sessionWarningPercent, sessionWarningPercent)
        loginItemSetUp = value(.loginItemSetUp, loginItemSetUp)
    }

    /// A pet list, skipping names this build does not know rather than dropping the whole list.
    private static func pets(_ c: KeyedDecodingContainer<CodingKeys>, _ key: CodingKeys) -> [Pet] {
        let names = (try? c.decodeIfPresent([String].self, forKey: key)) ?? []
        return names.compactMap(Pet.init(rawValue:))
    }
}
