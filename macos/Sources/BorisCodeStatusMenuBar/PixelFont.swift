import CoreGraphics

/// A 5×7 uppercase bitmap font for the cards' headings, kept as data for the same reasons as the
/// sprites: no font file to ship or license, and it renders crisp at any integer pixel size, which
/// a TrueType font at 7px never does. Generated from the Windows build's `PixelFont.cs`; edit both.
///
/// Deliberately small. It covers what the cards print — letters, digits and a little punctuation —
/// and draws anything else as '?', so an unexpected character is visible rather than silently lost.
///
/// Sizes are in the caller's units (points on a card), and every stroke is a whole number of them.
enum PixelFont {
    static let glyphWidth = 5
    static let glyphHeight = 7

    // Each glyph is seven rows of five cells, rows separated by '|', '1' is ink.
    private static let glyphs: [Character: [[Bool]]] = [
        "A": "01110|10001|10001|11111|10001|10001|10001",
        "B": "11110|10001|10001|11110|10001|10001|11110",
        "C": "01110|10001|10000|10000|10000|10001|01110",
        "D": "11110|10001|10001|10001|10001|10001|11110",
        "E": "11111|10000|10000|11110|10000|10000|11111",
        "F": "11111|10000|10000|11110|10000|10000|10000",
        "G": "01110|10001|10000|10111|10001|10001|01111",
        "H": "10001|10001|10001|11111|10001|10001|10001",
        "I": "01110|00100|00100|00100|00100|00100|01110",
        "J": "00111|00010|00010|00010|00010|10010|01100",
        "K": "10001|10010|10100|11000|10100|10010|10001",
        "L": "10000|10000|10000|10000|10000|10000|11111",
        "M": "10001|11011|10101|10101|10001|10001|10001",
        "N": "10001|10001|11001|10101|10011|10001|10001",
        "O": "01110|10001|10001|10001|10001|10001|01110",
        "P": "11110|10001|10001|11110|10000|10000|10000",
        "Q": "01110|10001|10001|10001|10101|10010|01101",
        "R": "11110|10001|10001|11110|10100|10010|10001",
        "S": "01111|10000|10000|01110|00001|00001|11110",
        "T": "11111|00100|00100|00100|00100|00100|00100",
        "U": "10001|10001|10001|10001|10001|10001|01110",
        "V": "10001|10001|10001|10001|10001|01010|00100",
        "W": "10001|10001|10001|10101|10101|10101|01010",
        "X": "10001|10001|01010|00100|01010|10001|10001",
        "Y": "10001|10001|01010|00100|00100|00100|00100",
        "Z": "11111|00001|00010|00100|01000|10000|11111",
        "0": "01110|10001|10011|10101|11001|10001|01110",
        "1": "00100|01100|00100|00100|00100|00100|01110",
        "2": "01110|10001|00001|00010|00100|01000|11111",
        "3": "11111|00010|00100|00010|00001|10001|01110",
        "4": "00010|00110|01010|10010|11111|00010|00010",
        "5": "11111|10000|11110|00001|00001|10001|01110",
        "6": "00110|01000|10000|11110|10001|10001|01110",
        "7": "11111|00001|00010|00100|01000|01000|01000",
        "8": "01110|10001|10001|01110|10001|10001|01110",
        "9": "01110|10001|10001|01111|00001|00010|01100",
        " ": "00000|00000|00000|00000|00000|00000|00000",
        "/": "00001|00001|00010|00100|01000|10000|10000",
        "-": "00000|00000|00000|11111|00000|00000|00000",
        ".": "00000|00000|00000|00000|00000|01100|01100",
        ":": "00000|01100|01100|00000|01100|01100|00000",
        "!": "00100|00100|00100|00100|00100|00000|00100",
        "?": "01110|10001|00001|00010|00100|00000|00100",
        "%": "11001|11010|00010|00100|01000|01011|10011",
        ">": "10000|01000|00100|00010|00100|01000|10000",
        "'": "00100|00100|01000|00000|00000|00000|00000",
    ].mapValues { $0.split(separator: "|").map { $0.map { $0 == "1" } } }

    /// Width of `text`. `tracking` is the gap between glyphs in font pixels; bold adds one font
    /// pixel to every glyph.
    static func measure(_ text: String, pixel: CGFloat, tracking: Int, bold: Bool = false) -> CGFloat {
        guard !text.isEmpty else { return 0 }
        let advance = glyphWidth + (bold ? 1 : 0) + tracking
        return CGFloat(text.count * advance - tracking) * pixel
    }

    /// Draws one line with its top-left at (`x`, `y`) in a y-down context. Bold is a second strike
    /// one font pixel to the right — the usual way pixel fonts embolden, and it keeps every stroke
    /// on the grid.
    static func draw(_ text: String, in context: CGContext, x: CGFloat, y: CGFloat, pixel: CGFloat, tracking: Int, color: CGColor, bold: Bool = false) {
        context.setFillColor(color)
        let advance = CGFloat(glyphWidth + (bold ? 1 : 0) + tracking) * pixel
        var x = x

        for character in text.uppercased() {
            let rows = glyphs[character] ?? glyphs["?"]!
            for (row, cells) in rows.enumerated() {
                for (column, ink) in cells.enumerated() where ink {
                    context.fill(CGRect(x: x + CGFloat(column) * pixel, y: y + CGFloat(row) * pixel, width: (bold ? 2 : 1) * pixel, height: pixel))
                }
            }
            x += advance
        }
    }

    /// Greedy word wrap to a width. A word too long for a line gets one to itself.
    static func wrap(_ text: String, maxWidth: CGFloat, pixel: CGFloat, tracking: Int, bold: Bool = false) -> [String] {
        var lines: [String] = []
        var current = ""

        for word in text.split(separator: " ") {
            let candidate = current.isEmpty ? String(word) : current + " " + word
            if !current.isEmpty, measure(candidate, pixel: pixel, tracking: tracking, bold: bold) > maxWidth {
                lines.append(current)
                current = String(word)
            } else {
                current = candidate
            }
        }

        if !current.isEmpty {
            lines.append(current)
        }
        return lines
    }
}
