import Foundation
import Testing
@testable import BorisCodeStatusCore

struct SettingsTests {
    let url = FileManager.default.temporaryDirectory
        .appending(path: "BorisCodeStatusTests-\(UUID().uuidString)")
        .appending(path: "settings.json")

    @Test func aMissingFileYieldsTheDefaultsAndWritesNothing() {
        #expect(SettingsStore.load(url: url) == AppSettings())
        #expect(NotificationPreference.areEnabled(url: url))
        #expect(!NotificationPreference.idleCardEnabled(url: url))
        #expect(!SoundPreference.isEnabled(.waiting, url: url))
        #expect(!UpdateCheckPreference.isEnabled(url: url))
        #expect(PetPreference.get(url: url) == .cat)
        #expect(SessionWarningPreference.get(url: url) == 80)
        #expect(!FileManager.default.fileExists(atPath: url.path))
    }

    @Test func aChangeSurvivesAReload() {
        #expect(PetPreference.trySet(.goat, url: url))
        #expect(StatusCardPreference.trySavePosition(CardPosition(x: 40, y: 900), url: url))
        #expect(ClickTargetPreference.trySet("  com.apple.Terminal ", url: url))

        #expect(PetPreference.get(url: url) == .goat)
        #expect(StatusCardPreference.position(url: url) == CardPosition(x: 40, y: 900))
        #expect(ClickTargetPreference.get(url: url) == "com.apple.Terminal")
    }

    @Test func readsAFileTheWindowsBuildWrote() throws {
        try write("""
        {
          "http_enabled": true,
          "usage_api_enabled": false,
          "notifications_enabled": false,
          "idle_card_enabled": true,
          "pet": "Goat",
          "waiting_alternate_sound_pets": ["Cat", "Duck"],
          "status_card_position": { "x": -1200, "y": 300 },
          "session_warning_percent": 90
        }
        """)

        let settings = SettingsStore.load(url: url)
        #expect(!settings.notificationsEnabled)
        #expect(settings.idleCardEnabled)
        #expect(settings.pet == .goat)
        #expect(settings.waitingAlternateSoundPets == [.cat, .duck])
        #expect(settings.statusCardPosition == CardPosition(x: -1200, y: 300))
        #expect(settings.sessionWarningPercent == 90)
    }

    @Test func aBadMemberCostsOnlyItself() throws {
        try write(#"{ "pet": "Dragon", "idle_sound_enabled": "yes", "idle_card_enabled": true, "idle_alternate_sound_pets": ["Dragon", "Bot"] }"#)

        let settings = SettingsStore.load(url: url)
        #expect(settings.pet == .cat)
        #expect(!settings.idleSoundEnabled)
        #expect(settings.idleCardEnabled)
        #expect(settings.idleAlternateSoundPets == [.bot])
    }

    @Test func aCorruptFileIsBackedUpBeforeItIsReplaced() throws {
        try write("{ half a file")

        #expect(SettingsStore.load(url: url) == AppSettings())
        #expect(PetPreference.trySet(.duck, url: url))

        let backup = try String(contentsOf: URL(filePath: url.path + ".bak"), encoding: .utf8)
        #expect(backup == "{ half a file")
        #expect(PetPreference.get(url: url) == .duck)
    }

    @Test func alternateSoundsAreKeptPerPetAndPerMoment() {
        SoundPreference.trySetAlternate(.waiting, .dog, true, url: url)
        SoundPreference.trySetAlternate(.waiting, .cat, true, url: url)
        SoundPreference.trySetAlternate(.waiting, .dog, false, url: url)

        #expect(!SoundPreference.usesAlternate(.waiting, .dog, url: url))
        #expect(SoundPreference.usesAlternate(.waiting, .cat, url: url))
        #expect(!SoundPreference.usesAlternate(.idle, .cat, url: url))
    }

    @Test func theWarningThresholdIsClampedAndAnOutOfRangeValueReadsAsTheDefault() throws {
        SessionWarningPreference.trySet(20, url: url)
        #expect(SessionWarningPreference.get(url: url) == 50)

        try write(#"{ "session_warning_percent": 140 }"#)
        #expect(SessionWarningPreference.get(url: url) == 80)
    }

    @Test func writesTheWindowsMemberNames() throws {
        PetPreference.trySet(.bot, url: url)

        let root = try JSONValue.parse(String(contentsOf: url, encoding: .utf8))
        let keys = Set(root.objectValue?.map(\.0) ?? [])
        #expect(keys.contains("pet"))
        #expect(keys.contains("notifications_enabled"))
        #expect(keys.contains("session_warning_percent"))
        // Absent rather than null when nothing is chosen. Windows writes null; both builds read either.
        #expect(!keys.contains("click_target_app"))
    }

    private func write(_ text: String) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(text.utf8).write(to: url)
    }
}
