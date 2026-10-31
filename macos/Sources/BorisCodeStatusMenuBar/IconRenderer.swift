import AppKit
import BorisCodeStatusCore

/// Draws the menu-bar glyph at runtime rather than shipping image assets, so the icon can encode
/// live data: the pet is the activity state and the ring behind it is session quota used.
///
/// The same drawing as the Windows tray's `TrayIconRenderer`: ring first, antialiased; pet over
/// it, hard-edged, filling the icon so the ring shows only in the gaps.
public enum IconRenderer {
    /// The icon's size in points. The menu bar is 22–24pt tall; 16pt is the standard glyph size.
    public static let pointSize = 16

    static let trackColor = PixelColor(0xFF, 0xFF, 0xFF, alpha: 0x50)
    static let quotaLowColor = PixelColor(0x5C, 0xC2, 0x6A, alpha: 0xF0)
    static let quotaMidColor = PixelColor(0xF5, 0xB9, 0x42, alpha: 0xF0)
    static let quotaHighColor = PixelColor(0xE5, 0x48, 0x48, alpha: 0xF0)

    /// Green below 50%, amber from 50% to 75% inclusive, red above 75%.
    public static func quotaColor(for used: Double) -> PixelColor {
        switch used {
        case ..<50: quotaLowColor
        case ...75: quotaMidColor
        default: quotaHighColor
        }
    }

    /// The menu-bar image, carrying an exact 1x (16px) and 2x (32px) bitmap so AppKit never
    /// resamples it: the 2x one is the 16px sprite doubled, which is what the design's 32px icon is.
    /// Not a template image — the colours are the point.
    @MainActor
    public static func image(dog: DogState, sessionUsedPercentage: Double?, pet: Pet = .dog, frame: Int = 0) -> NSImage {
        let image = NSImage(size: NSSize(width: pointSize, height: pointSize))
        for scale in [1, 2] {
            guard let bitmap = render(dog: dog, sessionUsedPercentage: sessionUsedPercentage, pet: pet, frame: frame, pixels: pointSize * scale)
            else { continue }
            let rep = NSBitmapImageRep(cgImage: bitmap)
            rep.size = image.size
            image.addRepresentation(rep)
        }
        image.isTemplate = false
        return image
    }

    /// The bare sprite, no ring, at `cell` points a sprite pixel, with exact 1x and 2x bitmaps —
    /// for the settings window's pet choices.
    @MainActor
    public static func spriteImage(pet: Pet, dog: DogState, cell: Int) -> NSImage {
        let points = PetSprites.size * cell
        let image = NSImage(size: NSSize(width: points, height: points))
        for scale in [1, 2] {
            let pixels = points * scale
            guard let context = CGContext(
                data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0,
                space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
            ) else { continue }
            context.translateBy(x: 0, y: CGFloat(pixels))
            context.scaleBy(x: 1, y: -1)
            drawPet(context, pixels: pixels, dog: dog, pet: pet, frame: 0)
            guard let bitmap = context.makeImage() else { continue }
            let rep = NSBitmapImageRep(cgImage: bitmap)
            rep.size = image.size
            image.addRepresentation(rep)
        }
        return image
    }

    /// Renders one bitmap `pixels` square.
    public static func render(dog: DogState, sessionUsedPercentage: Double?, pet: Pet, frame: Int, pixels: Int) -> CGImage? {
        guard let context = CGContext(
            data: nil,
            width: pixels,
            height: pixels,
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }

        // Flip to y-down so sprite rows read top to bottom, as they are written.
        context.translateBy(x: 0, y: CGFloat(pixels))
        context.scaleBy(x: 1, y: -1)

        drawQuotaRing(context, pixels: pixels, used: sessionUsedPercentage)
        drawPet(context, pixels: pixels, dog: dog, pet: pet, frame: frame)
        return context.makeImage()
    }

    /// An arc behind the glyph showing how much of the five-hour window is spent.
    private static func drawQuotaRing(_ context: CGContext, pixels: Int, used: Double?) {
        context.setShouldAntialias(true)

        // An eighth of the icon: 2px at 16px, 4px at 32px. Inset by half its width so the stroke
        // runs right to the edge without being clipped by it.
        let width = CGFloat(pixels) / 8
        let ring = CGRect(x: width / 2, y: width / 2, width: CGFloat(pixels) - width, height: CGFloat(pixels) - width)
        context.setLineWidth(width)

        context.setStrokeColor(cgColor(trackColor))
        context.strokeEllipse(in: ring)

        guard let used else { return }
        let fraction = min(max(used, 0), 100) / 100
        guard fraction > 0 else { return }

        // Start at twelve o'clock and fill clockwise, the way a gauge reads. In the flipped
        // context, increasing angles run clockwise on screen.
        let start = -CGFloat.pi / 2
        context.setLineCap(.round)
        context.setStrokeColor(cgColor(quotaColor(for: used)))
        context.addArc(
            center: CGPoint(x: ring.midX, y: ring.midY),
            radius: ring.width / 2,
            startAngle: start,
            endAngle: start + CGFloat(fraction) * 2 * .pi,
            clockwise: false
        )
        context.strokePath()
    }

    /// Fills the sprite as solid blocks of whole pixels — never an interpolated scale, which is
    /// what destroys pixel art.
    private static func drawPet(_ context: CGContext, pixels: Int, dog: DogState, pet: Pet, frame: Int) {
        context.setShouldAntialias(false)

        let (grid, palette) = PetSprites.tray(dog, pet, frame: frame)
        let scale = max(1, pixels / PetSprites.size)
        let origin = (pixels - PetSprites.size * scale) / 2

        for (y, row) in grid.enumerated() {
            for (x, cell) in row.enumerated() {
                let index = PetSprites.index(of: cell)
                guard index > 0, index < palette.count else { continue }
                context.setFillColor(cgColor(palette[index]))
                context.fill(CGRect(x: origin + x * scale, y: origin + y * scale, width: scale, height: scale))
            }
        }
    }

    static func cgColor(_ color: PixelColor) -> CGColor {
        CGColor(
            srgbRed: CGFloat(color.red) / 255,
            green: CGFloat(color.green) / 255,
            blue: CGFloat(color.blue) / 255,
            alpha: CGFloat(color.alpha) / 255
        )
    }
}
