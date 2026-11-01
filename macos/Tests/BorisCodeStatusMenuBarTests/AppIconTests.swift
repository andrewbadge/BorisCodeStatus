import AppKit
import Foundation
import Testing
@testable import BorisCodeStatusMenuBar

/// Renders every size of the app icon. With `APP_ICON_DIR` set, writes them as an `.iconset` for
/// `scripts/make-icon.sh` to turn into `Resources/AppIcon.icns`; otherwise only checks the sizes.
struct AppIconTests {
    @Test func rendersEveryIconsetSize() throws {
        let directory = ProcessInfo.processInfo.environment["APP_ICON_DIR"].map { URL(filePath: $0) }
        if let directory {
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        }

        for (name, pixels) in AppIcon.iconsetSizes {
            let image = try #require(AppIcon.render(pixels: pixels))
            #expect(image.width == pixels && image.height == pixels)

            if let directory {
                let png = try #require(NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]))
                try png.write(to: directory.appending(path: "\(name).png"))
            }
        }
    }

    /// The corners are outside the rounded tile, so they must stay transparent: macOS draws no
    /// mask of its own over a Mac app's icon.
    @Test func leavesTheCornersClear() throws {
        let image = try #require(AppIcon.render(pixels: 256))
        let rep = NSBitmapImageRep(cgImage: image)
        #expect(rep.colorAt(x: 2, y: 2)?.alphaComponent == 0)
        #expect(rep.colorAt(x: 128, y: 128)?.alphaComponent == 1)
    }
}
