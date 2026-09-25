using System.Drawing;

namespace BorisCodeStatus.Tray;

/// <summary>
/// A 5×7 uppercase bitmap font for the notification card's headings, kept as data for the same
/// reasons as <see cref="DogSprites"/>: no font file to ship or license, and it renders crisp at
/// any integer pixel size, which a TrueType font at 7px never does.
///
/// Deliberately small. It covers what the card prints — letters, digits and a little punctuation —
/// and draws anything else as '?', so an unexpected character is visible rather than silently lost.
/// </summary>
internal static class PixelFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    // Each glyph is seven rows of five cells, rows separated by '|', '1' is ink.
    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['A'] = "01110|10001|10001|11111|10001|10001|10001",
        ['B'] = "11110|10001|10001|11110|10001|10001|11110",
        ['C'] = "01110|10001|10000|10000|10000|10001|01110",
        ['D'] = "11110|10001|10001|10001|10001|10001|11110",
        ['E'] = "11111|10000|10000|11110|10000|10000|11111",
        ['F'] = "11111|10000|10000|11110|10000|10000|10000",
        ['G'] = "01110|10001|10000|10111|10001|10001|01111",
        ['H'] = "10001|10001|10001|11111|10001|10001|10001",
        ['I'] = "01110|00100|00100|00100|00100|00100|01110",
        ['J'] = "00111|00010|00010|00010|00010|10010|01100",
        ['K'] = "10001|10010|10100|11000|10100|10010|10001",
        ['L'] = "10000|10000|10000|10000|10000|10000|11111",
        ['M'] = "10001|11011|10101|10101|10001|10001|10001",
        ['N'] = "10001|10001|11001|10101|10011|10001|10001",
        ['O'] = "01110|10001|10001|10001|10001|10001|01110",
        ['P'] = "11110|10001|10001|11110|10000|10000|10000",
        ['Q'] = "01110|10001|10001|10001|10101|10010|01101",
        ['R'] = "11110|10001|10001|11110|10100|10010|10001",
        ['S'] = "01111|10000|10000|01110|00001|00001|11110",
        ['T'] = "11111|00100|00100|00100|00100|00100|00100",
        ['U'] = "10001|10001|10001|10001|10001|10001|01110",
        ['V'] = "10001|10001|10001|10001|10001|01010|00100",
        ['W'] = "10001|10001|10001|10101|10101|10101|01010",
        ['X'] = "10001|10001|01010|00100|01010|10001|10001",
        ['Y'] = "10001|10001|01010|00100|00100|00100|00100",
        ['Z'] = "11111|00001|00010|00100|01000|10000|11111",
        ['0'] = "01110|10001|10011|10101|11001|10001|01110",
        ['1'] = "00100|01100|00100|00100|00100|00100|01110",
        ['2'] = "01110|10001|00001|00010|00100|01000|11111",
        ['3'] = "11111|00010|00100|00010|00001|10001|01110",
        ['4'] = "00010|00110|01010|10010|11111|00010|00010",
        ['5'] = "11111|10000|11110|00001|00001|10001|01110",
        ['6'] = "00110|01000|10000|11110|10001|10001|01110",
        ['7'] = "11111|00001|00010|00100|01000|01000|01000",
        ['8'] = "01110|10001|10001|01110|10001|10001|01110",
        ['9'] = "01110|10001|10001|01111|00001|00010|01100",
        [' '] = "00000|00000|00000|00000|00000|00000|00000",
        ['/'] = "00001|00001|00010|00100|01000|10000|10000",
        ['-'] = "00000|00000|00000|11111|00000|00000|00000",
        ['.'] = "00000|00000|00000|00000|00000|01100|01100",
        [':'] = "00000|01100|01100|00000|01100|01100|00000",
        ['!'] = "00100|00100|00100|00100|00100|00000|00100",
        ['?'] = "01110|10001|00001|00010|00100|00000|00100",
        ['\''] = "00100|00100|01000|00000|00000|00000|00000",
    };

    /// <summary>
    /// Width in device pixels. <paramref name="tracking"/> is the gap between glyphs in font
    /// pixels; bold adds one font pixel to every glyph.
    /// </summary>
    public static int Measure(string text, int pixel, int tracking, bool bold = false)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        var advance = GlyphWidth + (bold ? 1 : 0) + tracking;
        return ((text.Length * advance) - tracking) * pixel;
    }

    /// <summary>
    /// Draws one line. Bold is a second strike one font pixel to the right — the usual way pixel
    /// fonts embolden, and it keeps every stroke on the grid.
    /// </summary>
    public static void Draw(Graphics graphics, string text, int x, int y, int pixel, int tracking, Color color, bool bold = false)
    {
        using var brush = new SolidBrush(color);
        var advance = (GlyphWidth + (bold ? 1 : 0) + tracking) * pixel;

        foreach (var character in text.ToUpperInvariant())
        {
            var rows = (Glyphs.TryGetValue(character, out var glyph) ? glyph : Glyphs['?']).Split('|');
            for (var row = 0; row < GlyphHeight; row++)
            {
                for (var column = 0; column < GlyphWidth; column++)
                {
                    if (rows[row][column] != '1')
                    {
                        continue;
                    }

                    var width = (bold ? 2 : 1) * pixel;
                    graphics.FillRectangle(brush, x + (column * pixel), y + (row * pixel), width, pixel);
                }
            }

            x += advance;
        }
    }

    /// <summary>Greedy word wrap to a device-pixel width. A word too long for a line gets one to itself.</summary>
    public static List<string> Wrap(string text, int maxWidth, int pixel, int tracking, bool bold = false)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && Measure(candidate, pixel, tracking, bold) > maxWidth)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }
}
