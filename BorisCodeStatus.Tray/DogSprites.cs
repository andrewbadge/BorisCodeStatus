using System.Drawing;
using BorisCodeStatus.Core.Models;

namespace BorisCodeStatus.Tray;

/// <summary>
/// The 8-bit dog, as palette-index grids rather than image files.
///
/// Transcribed pixel-for-pixel from the design's 16px tray icons. Kept as data in source for the
/// same reason the icon was drawn at runtime before: no binaries in the repo, nothing to keep in
/// sync with a build step, and the glyph stays greppable and diffable. The design's 32px and 64px
/// icons are these doubled and quadrupled, so the renderer integer-scales them to the tray size.
///
/// Each character is an index into a palette: '0' transparent through 'A' = 10. The tray sprites
/// use <see cref="TrayPalette"/>; <see cref="WaitingPortrait"/> uses <see cref="Palette"/>.
/// </summary>
internal static class DogSprites
{
    /// <summary>
    /// The notification card's eleven-colour palette, in the original design's order. Indices 4 and 5
    /// are the shade ramp; the rest is kept as a faithful copy rather than a subset that silently
    /// renumbers everything after it.
    /// </summary>
    public static readonly Color[] Palette =
    [
        Color.Transparent,              // 0
        Color.FromArgb(0x17, 0x15, 0x0F), // 1 outline / ink
        Color.FromArgb(0xF2, 0xED, 0xE4), // 2 cream fur
        Color.FromArgb(0xD9, 0x77, 0x57), // 3 orange patch
        Color.FromArgb(0xA6, 0x54, 0x3A), // 4 orange shade
        Color.FromArgb(0xCF, 0xC7, 0xB8), // 5 fur shade
        Color.FromArgb(0xE5, 0x8F, 0xA0), // 6 tongue
        Color.FromArgb(0xFF, 0xFF, 0xFF), // 7 eye glint
        Color.FromArgb(0x6F, 0xA9, 0x6A), // 8 idle green
        Color.FromArgb(0xE8, 0xB0, 0x4B), // 9 waiting amber
        Color.FromArgb(0x7A, 0x8A, 0xA3), // 10 sleeping slate
    ];

    /// <summary>The tray icons' palette, sampled exactly from the design's PNGs.</summary>
    public static readonly Color[] TrayPalette =
    [
        Color.Transparent,                // 0
        Color.FromArgb(0x2A, 0x1F, 0x1A), // 1 outline
        Color.FromArgb(0xF4, 0xEE, 0xE2), // 2 cream fur
        Color.FromArgb(0xE0, 0x72, 0x58), // 3 orange ears and patch
        Color.FromArgb(0x11, 0x11, 0x11), // 4 eyes and nose
        Color.FromArgb(0x6C, 0xC6, 0xF0), // 5 working badge, blue
        Color.FromArgb(0xF5, 0xB9, 0x42), // 6 waiting badge, amber
        Color.FromArgb(0xC8, 0xC8, 0xD7), // 7 sleeping badge, lavender
    ];

    public const int Size = 16;

    /// <summary>Blue square badge.</summary>
    private static readonly string[] Working =
    [
        "0000000000000000",
        "0110000000000110",
        "1331000000001331",
        "1333111111113331",
        "1333222222223331",
        "0132222222222310",
        "0122222223333210",
        "0122442223443210",
        "0122442223443210",
        "0122222442222210",
        "0122222222222210",
        "0012222222211111",
        "0001222222215551",
        "0000111111115551",
        "0000000000015551",
        "0000000000011111",
    ];

    /// <summary>No badge — the plain face.</summary>
    private static readonly string[] Idle =
    [
        "0000000000000000",
        "0110000000000110",
        "1331000000001331",
        "1333111111113331",
        "1333222222223331",
        "0132222222222310",
        "0122222223333210",
        "0122442223443210",
        "0122442223443210",
        "0122222442222210",
        "0122222222222210",
        "0012222222222100",
        "0001222222221000",
        "0000111111110000",
        "0000000000000000",
        "0000000000000000",
    ];

    /// <summary>Amber "!" badge — the pose that should catch the eye.</summary>
    private static readonly string[] Waiting =
    [
        "0000000000000000",
        "0110000000000110",
        "1331000000001331",
        "1333111111113331",
        "1333222222223331",
        "0132222222222310",
        "0122222223333210",
        "0122442223443210",
        "0122442223443210",
        "0122222442222210",
        "0122222222266666",
        "0012222222266166",
        "0001222222266166",
        "0000111111166666",
        "0000000000066166",
        "0000000000066666",
    ];

    /// <summary>Eyes shut, lavender "Z" badge.</summary>
    private static readonly string[] Sleeping =
    [
        "0000000000000000",
        "0110000000000110",
        "1331000000001331",
        "1333111111113331",
        "1333222222223331",
        "0132222222222310",
        "0122222223333210",
        "0122222223333210",
        "0122442223443210",
        "0122222442222210",
        "0122222222277777",
        "0012222222271117",
        "0001222222277717",
        "0000111111177177",
        "0000000000071117",
        "0000000000077777",
    ];

    /// <summary>
    /// The full-size waiting dog for the notification card — the 32×32 design sprite trimmed to
    /// its bounds (23×26), sitting up with the "!" bang. Sampled from the design's rendered card,
    /// where every sprite pixel is a clean 4×4 block of an exact palette colour, so this is the
    /// design's grid rather than a redrawing of it.
    ///
    /// Not tied to the tray size above: the card has room for the full portrait.
    /// It is still drawn at an integer pixel size only. Index 9 appears nowhere but the bang, which
    /// is what lets the card blink the bang alone by skipping that index.
    /// </summary>
    public static readonly string[] WaitingPortrait =
    [
        "00001000000000001000000",
        "00013100011100013100900",
        "00133111122211113310900",
        "00133122222222213310900",
        "01333222222333333331000",
        "01333222223333333331900",
        "13333222223333333333100",
        "13333227123371333333100",
        "01112221123311333111000",
        "00012221155511333100000",
        "00012222211122332100000",
        "00015222211122225100000",
        "00001522521522251000000",
        "00000152122155510000000",
        "00000012222211100000000",
        "00000122222221110000110",
        "00001222222222221001331",
        "00012222222233333101331",
        "00012222222233333313310",
        "00012222222233333313310",
        "00012222222233333331100",
        "00015222222233333331000",
        "00001522212233333310000",
        "00000122212233333100000",
        "00000155515533331000000",
        "00000011111111110000000",
    ];

    /// <summary>The palette index used only by the bang in <see cref="WaitingPortrait"/>.</summary>
    public const int BangIndex = 9;

    public static string[] For(DogState state) => state switch
    {
        DogState.Working => Working,
        DogState.Waiting => Waiting,
        DogState.Idle => Idle,
        _ => Sleeping,
    };

    /// <summary>Parses a grid character into a palette index. '0'–'9' then 'A' for 10.</summary>
    public static int IndexOf(char cell) => cell <= '9' ? cell - '0' : cell - 'A' + 10;
}
