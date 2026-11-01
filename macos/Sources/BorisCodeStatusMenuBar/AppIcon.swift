import BorisCodeStatusCore
import CoreGraphics

/// The app's Finder and Dock icon: the cat's card portrait on a rounded tile in the cards' own
/// colours. Drawn from the same sprite data as the menu bar, so it cannot drift from it.
///
/// Not drawn at run time: `scripts/make-icon.sh` renders every size into `Resources/AppIcon.icns`,
/// which is committed, so a build needs no extra step. Rerun the script after changing this.
public enum AppIcon {
    /// The sizes an `.iconset` holds, in pixels: 16 to 512 points at 1x and 2x.
    public static let iconsetSizes: [(name: String, pixels: Int)] = [16, 32, 128, 256, 512].flatMap { points in
        [("icon_\(points)x\(points)", points), ("icon_\(points)x\(points)@2x", points * 2)]
    }

    /// One bitmap `pixels` square.
    public static func render(pixels: Int) -> CGImage? {
        guard let context = CGContext(
            data: nil, width: pixels, height: pixels, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB) ?? CGColorSpaceCreateDeviceRGB(),
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else { return nil }

        context.translateBy(x: 0, y: CGFloat(pixels))
        context.scaleBy(x: 1, y: -1)

        // Apple's icon grid: the tile is 824 of 1024 with a 185 corner radius, leaving the margin
        // macOS expects so the icon sits level with every other app's.
        let size = CGFloat(pixels)
        let tile = CGRect(x: 0, y: 0, width: size, height: size).insetBy(dx: size * 100 / 1024, dy: size * 100 / 1024)
        let radius = size * 185 / 1024
        fill(context, CGPath(roundedRect: tile, cornerWidth: radius, cornerHeight: radius, transform: nil), CardPanel.borderColor)
        let border = max(1, (size * 16 / 1024).rounded())
        let screen = tile.insetBy(dx: border, dy: border)
        fill(context, CGPath(roundedRect: screen, cornerWidth: radius - border, cornerHeight: radius - border, transform: nil), CardPanel.screenColor)

        guard let (grid, palette) = PetSprites.portrait(.cat) else { return context.makeImage() }
        let rows = grid.count
        let columns = grid.map(\.count).max() ?? 0

        // Whole-pixel cells wherever they fit, as everywhere else the sprites are drawn; only the
        // two smallest sizes, too small for one pixel a cell, fall back to fractional cells.
        let exact = screen.height * 0.72 / CGFloat(rows)
        let cell = exact >= 1 ? exact.rounded(.down) : exact
        let width = CGFloat(columns) * cell
        let height = CGFloat(rows) * cell
        let originX = (screen.midX - width / 2).rounded()
        let originY = (screen.midY - height / 2).rounded()

        context.setShouldAntialias(exact < 1)
        for (y, row) in grid.enumerated() {
            for (x, character) in row.enumerated() {
                let index = PetSprites.index(of: character)
                guard index > 0, index < palette.count else { continue }
                let rect = CGRect(x: originX + CGFloat(x) * cell, y: originY + CGFloat(y) * cell, width: cell, height: cell)
                context.setFillColor(IconRenderer.cgColor(palette[index]))
                context.fill(rect)
            }
        }

        return context.makeImage()
    }

    private static func fill(_ context: CGContext, _ path: CGPath, _ color: PixelColor) {
        context.addPath(path)
        context.setFillColor(IconRenderer.cgColor(color))
        context.fillPath()
    }
}
