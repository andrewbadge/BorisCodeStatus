import AppKit
import Foundation
import Testing
@testable import BorisCodeStatusCore
@testable import BorisCodeStatusMenuBar

struct WaitingPromptTests {
    @Test func aPermissionRequestAsksYesOrNo() {
        let prompt = WaitingPrompt.from("Claude needs your permission to use Bash")
        #expect(prompt.title == "Permission needed")
        #expect(prompt.detail == "Claude needs your permission to use Bash")
        #expect(prompt.hint == "Y / N")
    }

    @Test func anIdlePromptAsksForInput() {
        #expect(WaitingPrompt.from("Claude is waiting for your input").title == "Input needed")
    }

    @Test func anythingElseFallsBackToAPlainHeading() {
        #expect(WaitingPrompt.from("Something new").title == "Claude needs you")
        #expect(WaitingPrompt.from("   ").detail == WaitingPrompt.fallbackDetail)
        #expect(WaitingPrompt.from(nil).hint == nil)
    }
}

@MainActor
struct CardLayoutTests {
    let area = CGRect(x: 0, y: 0, width: 1440, height: 875)

    @Test func aCardOnScreenStaysWhereItIs() {
        let card = CGRect(x: 200, y: 300, width: 354, height: 282)
        #expect(CardPanel.keepOnScreen(card, area) == card.origin)
    }

    @Test func aCardFromAnUnpluggedDisplayIsPulledBack() {
        #expect(CardPanel.keepOnScreen(CGRect(x: 3000, y: -500, width: 354, height: 282), area) == CGPoint(x: 1440 - 354, y: 0))
        #expect(CardPanel.keepOnScreen(CGRect(x: -900, y: 2000, width: 236, height: 56), area) == CGPoint(x: 0, y: 875 - 56))
    }

    @Test func theFullCardIsTheDesignsSize() {
        #expect(CardPanel.fullSize == CGSize(width: 354, height: 282))
    }

    @Test func durationsReadAsTheWindowsBuildWritesThem() {
        #expect(MenuBarController.formatDuration(42) == "42m")
        #expect(MenuBarController.formatDuration(135) == "2h 15m")
        #expect(MenuBarController.formatDuration(60 * 24 * 3 + 90) == "3d 1h")
    }
}

struct PortraitAndSoundTests {
    static let repoRoot = URL(filePath: #filePath).deletingLastPathComponent().appending(path: "../../..").standardized

    /// The card's portraits only use indices their palette defines, like the tray sprites.
    @Test(arguments: Pet.allCases)
    func everyPortraitIsWellFormed(pet: Pet) {
        let portraits = (0..<PetSprites.wagFrames).compactMap { PetSprites.portrait(pet, frame: $0) }
            + [(SpriteData.waitingPortrait, SpriteData.palette), (SpriteData.catSleepingPortrait, SpriteData.catPalette)]
        for (grid, palette) in portraits {
            let width = grid.first?.count ?? 0
            for row in grid {
                #expect(row.count == width, "\(pet): \(row)")
                #expect(row.allSatisfy { $0.isHexDigit && PetSprites.index(of: $0) < palette.count }, "\(pet): \(row)")
            }
        }
    }

    /// The bundle copies the Windows build's recordings by name, so a rename there would silence a
    /// pet here without this.
    @Test(arguments: Pet.allCases)
    func everySoundHasARecording(pet: Pet) {
        for alternate in [false, true] {
            let file = Self.repoRoot.appending(path: "BorisCodeStatus.Tray/Sounds").appending(path: WaitingSound.fileName(pet, alternate: alternate))
            #expect(FileManager.default.fileExists(atPath: file.path), "\(file.lastPathComponent)")
        }
    }
}

/// Renders the cards to PNGs when `CARD_PREVIEW_DIR` is set, so they can be looked at without
/// waiting for Claude to ask for something. Otherwise only checks that drawing does not trap.
@MainActor
struct CardPreviewTests {
    @Test func rendersEachCard() throws {
        let samples: [(String, Pet, Bool, Bool, CardContent)] = [
            ("waiting-dog", .dog, false, false, .from(.from("Claude needs your permission to use Bash"))),
            ("waiting-cat", .cat, false, false, .from(.from("Claude is waiting for your input"))),
            ("waiting-duck", .duck, false, false, .from(.from(nil))),
            ("idle-cat", .cat, false, false, CardContent(state: "Idle", accent: IconRenderer.quotaColor(for: 0), title: "Your turn", detail: "Claude has finished and is waiting for your next message.", hint: nil, dog: .idle)),
            ("sleeping-cat", .cat, false, false, CardContent(state: "Sleeping", accent: CardContent.accent(for: .sleeping), title: "5h 31% used", detail: "Resets in 2h 15m\nWeek: 12% used", hint: "5H", dog: .sleeping, gauge: 31)),
            ("warning-goat", .goat, true, false, CardContent(state: "Warning", accent: IconRenderer.quotaColor(for: 84), title: "5h 84% used", detail: "Past your 80% warning.\nResets in 1h 2m.", hint: "5H", dog: .waiting, gauge: 84)),
            ("mini-cat", .cat, false, true, CardContent(state: "Working", accent: CardContent.accent(for: .working), title: "", detail: "", hint: "5H", dog: .working, gauge: 62)),
        ]

        let directory = ProcessInfo.processInfo.environment["CARD_PREVIEW_DIR"].map { URL(filePath: $0) }
        for (name, pet, persistent, mini, content) in samples {
            let card = CardPanel()
            card.pet = pet
            card.persistent = persistent
            card.linkText = persistent ? "Usage" : nil
            card.mini = mini
            card.pinned = mini
            card.show(content)

            let view = try #require(card.contentView)
            let rep = try #require(view.bitmapImageRepForCachingDisplay(in: view.bounds))
            view.cacheDisplay(in: view.bounds, to: rep)
            card.dismiss()

            #expect(rep.size == (mini ? CardPanel.miniSize : CardPanel.fullSize))
            if let directory, let png = rep.representation(using: .png, properties: [:]) {
                try png.write(to: directory.appending(path: "\(name).png"))
            }
        }
    }
}
