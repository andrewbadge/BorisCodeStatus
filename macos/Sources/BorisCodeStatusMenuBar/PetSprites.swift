import BorisCodeStatusCore
import Foundation

/// One opaque sRGB colour in a sprite palette, or `clear` for index 0.
public struct PixelColor: Equatable, Sendable {
    public var red: UInt8
    public var green: UInt8
    public var blue: UInt8
    public var alpha: UInt8

    public init(_ red: UInt8, _ green: UInt8, _ blue: UInt8, alpha: UInt8 = 0xFF) {
        self.red = red
        self.green = green
        self.blue = blue
        self.alpha = alpha
    }

    public static let clear = PixelColor(0, 0, 0, alpha: 0)
}

/// Picks the grid and palette for a pet in a pose. The grids themselves are in `SpriteData`,
/// generated from the Windows build's `DogSprites.cs`.
public enum PetSprites {
    /// Every tray sprite is 16 by 16; larger sizes are whole-pixel integer scales of it.
    public static let size = 16

    /// How long each of the goat's tongue frames shows. 250ms is a four-beat-a-second wag:
    /// lively, not frantic.
    public static let wagInterval: TimeInterval = 0.25

    /// The goat's tongue frames: swung left, then swung right.
    public static let wagFrames = 2

    /// Which tongue frame shows at `now`. A pure function of the instant, so every surface wags in
    /// step, and a test can pin it.
    public static func wagFrame(at now: Date) -> Int {
        Int((now.timeIntervalSince1970 / wagInterval).rounded(.down)) % wagFrames
    }

    /// Whether this pet in this pose has more than one frame: only the goat, and only while Claude
    /// is busy — working or waiting. Lets the menu bar skip redrawing a still pose four times a second.
    public static func wags(_ pet: Pet, _ dog: DogState) -> Bool {
        pet == .goat && (dog == .working || dog == .waiting)
    }

    /// The menu-bar sprite for a pose, as the chosen pet, with the palette it indexes. `frame` is
    /// the goat's `wagFrame`; every other pet is a still image and ignores it.
    public static func tray(_ dog: DogState, _ pet: Pet, frame: Int = 0) -> (grid: [String], palette: [PixelColor]) {
        let f = abs(frame % wagFrames)
        switch pet {
        case .dog:
            return (pick(dog, SpriteData.idle, SpriteData.working, SpriteData.waiting, SpriteData.sleeping), SpriteData.trayPalette)
        case .cat:
            return (pick(dog, SpriteData.catIdle, SpriteData.catWorking, SpriteData.catWaiting, SpriteData.catSleeping), SpriteData.catPalette)
        case .bot:
            return (pick(dog, SpriteData.botIdle, SpriteData.botWorking, SpriteData.botWaiting, SpriteData.botSleeping), SpriteData.botPalette)
        case .duck:
            return (pick(dog, SpriteData.duckIdle, SpriteData.duckWorking, SpriteData.duckWaiting, SpriteData.duckSleeping), SpriteData.duckPalette)
        case .goat:
            return (pick(dog, SpriteData.goatIdle[f], SpriteData.goatWorking[f], SpriteData.goatWaiting[f], SpriteData.goatSleeping[f]), SpriteData.goatPalette)
        }
    }

    /// Parses a grid character into a palette index: '0'–'9', then 'A' for 10 up to 'F'.
    /// Anything else reads as transparent rather than trapping.
    public static func index(of cell: Character) -> Int {
        cell.hexDigitValue ?? 0
    }

    private static func pick(_ dog: DogState, _ idle: [String], _ working: [String], _ waiting: [String], _ sleeping: [String]) -> [String] {
        switch dog {
        case .working: working
        case .waiting: waiting
        case .idle: idle
        case .sleeping: sleeping
        }
    }
}
