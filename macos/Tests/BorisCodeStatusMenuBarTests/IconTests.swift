import AppKit
import Foundation
import Testing
@testable import BorisCodeStatusCore
@testable import BorisCodeStatusMenuBar

struct SpriteTests {
    static let poses: [DogState] = [.idle, .working, .waiting, .sleeping]

    /// Every grid is 16 by 16 and only uses indices its palette defines: a typo in a transcribed
    /// row would otherwise draw transparent holes or the wrong colour, silently.
    @Test(arguments: Pet.allCases)
    func everyTraySpriteIsWellFormed(pet: Pet) {
        for dog in Self.poses {
            for frame in 0..<PetSprites.wagFrames {
                let (grid, palette) = PetSprites.tray(dog, pet, frame: frame)
                #expect(grid.count == PetSprites.size, "\(pet) \(dog)")
                for row in grid {
                    #expect(row.count == PetSprites.size, "\(pet) \(dog): \(row)")
                    #expect(row.allSatisfy { $0.isHexDigit && PetSprites.index(of: $0) < palette.count }, "\(pet) \(dog): \(row)")
                }
            }
        }
    }

    @Test func eachPoseLooksDifferent() {
        for pet in Pet.allCases {
            let grids = Self.poses.map { PetSprites.tray($0, pet).grid }
            #expect(Set(grids).count == Self.poses.count, "\(pet)")
        }
    }

    @Test func onlyTheBusyGoatWags() {
        #expect(PetSprites.wags(.goat, .working))
        #expect(PetSprites.wags(.goat, .waiting))
        #expect(!PetSprites.wags(.goat, .idle))
        #expect(!PetSprites.wags(.dog, .working))
        #expect(PetSprites.tray(.working, .goat, frame: 0).grid != PetSprites.tray(.working, .goat, frame: 1).grid)
    }

    @Test func wagFrameAlternatesEveryQuarterSecond() {
        let start = Date(timeIntervalSince1970: 1_793_448_000)
        #expect(PetSprites.wagFrame(at: start) == 0)
        #expect(PetSprites.wagFrame(at: start.addingTimeInterval(0.25)) == 1)
        #expect(PetSprites.wagFrame(at: start.addingTimeInterval(0.5)) == 0)
    }
}

struct IconRendererTests {
    /// Reads one pixel back, top-left origin, as the sprite rows are written.
    func pixel(_ image: CGImage, _ x: Int, _ y: Int) -> PixelColor {
        var data = [UInt8](repeating: 0, count: 4)
        let context = CGContext(
            data: &data, width: 1, height: 1, bitsPerComponent: 8, bytesPerRow: 4,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        )!
        context.draw(image, in: CGRect(x: -x, y: y - image.height + 1, width: image.width, height: image.height))
        return PixelColor(data[0], data[1], data[2], alpha: data[3])
    }

    /// The sprite is blitted as whole pixels: every sprite cell comes out as its exact palette
    /// colour, doubled into a 2x2 block at 32px, with no blending at its edges.
    @Test(arguments: [16, 32])
    func blitsTheSpriteExactly(pixels: Int) throws {
        let image = try #require(IconRenderer.render(dog: .working, sessionUsedPercentage: nil, pet: .dog, frame: 0, pixels: pixels))
        let (grid, palette) = PetSprites.tray(.working, .dog)
        let scale = pixels / PetSprites.size

        for (y, row) in grid.enumerated() {
            for (x, cell) in row.enumerated() where PetSprites.index(of: cell) > 0 {
                let expected = palette[PetSprites.index(of: cell)]
                for dy in 0..<scale {
                    for dx in 0..<scale {
                        #expect(pixel(image, x * scale + dx, y * scale + dy) == expected, "(\(x), \(y))")
                    }
                }
            }
        }
    }

    @Test func quotaColourThresholds() {
        #expect(IconRenderer.quotaColor(for: 49.9) == IconRenderer.quotaLowColor)
        #expect(IconRenderer.quotaColor(for: 50) == IconRenderer.quotaMidColor)
        #expect(IconRenderer.quotaColor(for: 75) == IconRenderer.quotaMidColor)
        #expect(IconRenderer.quotaColor(for: 75.1) == IconRenderer.quotaHighColor)
    }

    /// The ring starts at twelve o'clock and fills clockwise: at 25% the top-right quadrant is
    /// coloured and the top-left is only the faint track.
    @Test func ringFillsClockwiseFromTwelve() throws {
        let image = try #require(IconRenderer.render(dog: .sleeping, sessionUsedPercentage: 25, pet: .dog, frame: 0, pixels: 32))
        // The dog covers most of the ring, so probe along the stroke's centre line for a point the
        // sprite leaves transparent: clockwise from twelve for the filled side, anticlockwise for
        // the unfilled side, within the first quarter either way.
        let (grid, _) = PetSprites.tray(.sleeping, .dog)
        func ringPoint(degrees: Double) -> (x: Int, y: Int)? {
            let radians = degrees * .pi / 180
            let x = Int((16 + 14 * sin(radians)).rounded(.down)), y = Int((16 - 14 * cos(radians)).rounded(.down))
            return PetSprites.index(of: Array(grid[y / 2])[x / 2]) == 0 ? (x, y) : nil
        }

        let filled = try #require(stride(from: 20.0, through: 70, by: 2).lazy.compactMap { ringPoint(degrees: $0) }.first)
        let unfilled = try #require(stride(from: -20.0, through: -70, by: -2).lazy.compactMap { ringPoint(degrees: $0) }.first)

        #expect(pixel(image, filled.x, filled.y).green > 0x80)
        #expect(pixel(image, unfilled.x, unfilled.y).alpha < 0x80)
    }

    @MainActor
    @Test func imageCarriesExact1xAnd2xBitmaps() {
        let image = IconRenderer.image(dog: .idle, sessionUsedPercentage: 40)

        #expect(image.size == NSSize(width: 16, height: 16))
        #expect(Set(image.representations.map(\.pixelsWide)) == [16, 32])
        #expect(!image.isTemplate)
    }
}

struct DirectoryWatcherTests {
    @Test func seesAnAtomicReplace() async throws {
        let directory = FileManager.default.temporaryDirectory.appending(path: "BorisCodeStatusTests-\(UUID().uuidString)")
        let file = directory.appending(path: "state.json")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try Data("{}".utf8).write(to: file, options: .atomic)

        let calls = Counter()
        let watcher = try #require(DirectoryWatcher(directory: directory, queue: DispatchQueue(label: "test")) { calls.increment() })
        defer { watcher.cancel() }

        // Two atomic replaces: the second is what a watch on the file itself would miss.
        for text in ["{\"a\":1}", "{\"a\":2}"] {
            try Data(text.utf8).write(to: file, options: .atomic)
            try await waitUntil { calls.value >= 1 }
            calls.reset()
        }
    }

    private func waitUntil(_ condition: () -> Bool) async throws {
        for _ in 0..<40 where !condition() {
            try await Task.sleep(for: .milliseconds(50))
        }
        #expect(condition())
    }
}

/// A thread-safe count for callbacks arriving on another queue.
final class Counter: @unchecked Sendable {
    private let lock = NSLock()
    private var count = 0

    var value: Int { lock.withLock { count } }
    func increment() { lock.withLock { count += 1 } }
    func reset() { lock.withLock { count = 0 } }
}
