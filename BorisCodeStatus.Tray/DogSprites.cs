using System.Drawing;
using BorisCodeStatus.Core.Models;

namespace BorisCodeStatus.Tray;

/// <summary>
/// The 8-bit dog, as palette-index grids rather than image files.
///
/// Transcribed pixel-for-pixel from the design's 16px tray icons. Kept as data in source for the
/// same reason the icon was drawn at runtime before: no image files in the repo, nothing to keep in
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

    /// <summary>
    /// The cat's palette, sampled exactly from the design's PNGs. Unlike the dog, idle has a badge
    /// of its own (green); the sleeping badge reuses the dog's lavender.
    /// </summary>
    public static readonly Color[] CatPalette =
    [
        Color.Transparent,                // 0
        Color.FromArgb(0x3B, 0x23, 0x14), // 1 outline
        Color.FromArgb(0xF2, 0x8C, 0x28), // 2 orange fur
        Color.FromArgb(0xF2, 0xA1, 0xA1), // 3 inner ear pink
        Color.FromArgb(0xC4, 0x5F, 0x12), // 4 dark orange stripes
        Color.FromArgb(0xFF, 0xFF, 0xFF), // 5 eye glint
        Color.FromArgb(0x1A, 0x12, 0x09), // 6 eyes
        Color.FromArgb(0xFF, 0xF1, 0xD6), // 7 cream muzzle and chest
        Color.FromArgb(0xE0, 0x70, 0x7A), // 8 nose
        Color.FromArgb(0x4C, 0xC2, 0x6A), // 9 idle badge, green
        Color.FromArgb(0x4F, 0xA8, 0xE6), // A working badge, blue
        Color.FromArgb(0xFF, 0xD2, 0x3F), // B waiting badge, yellow
        Color.FromArgb(0xC8, 0xC8, 0xD7), // C sleeping badge, lavender
        Color.FromArgb(0xA0, 0xC8, 0xEB), // D the sleeping cat's Z's
    ];

    /// <summary>
    /// The curled-up sleeping cat with its Z's, 22×14, from the design's 132×84 and 220×140
    /// sheets. For the status card only: it is wider than the 16px tray icon, so the tray keeps
    /// <see cref="CatSleeping"/>.
    /// </summary>
    public static readonly string[] CatSleepingPortrait =
    [
        "000000000000000000DDD0",
        "00110000000110000000D0",
        "0012100000121000000D00",
        "00132100012310DDD0D000",
        "0013221112231000D0DDD0",
        "00122244422210D0000000",
        "00122222222211DDD10000",
        "0012662226621222221000",
        "0014222222241242222100",
        "0012277877221224222210",
        "0012227772221222242221",
        "0001111111110222222221",
        "0017777177771242424241",
        "0011111111111111111111",
    ];

    private static readonly string[] CatIdle =
    [
        "0000000000000000",
        "0110000000000110",
        "0121000000001210",
        "0123100000013210",
        "0123211111123210",
        "0122222442222210",
        "1222224444222221",
        "1222562222562221",
        "1222662222662221",
        "1442222222222441",
        "1222277887722221",
        "1222277117711111",
        "0122222222219991",
        "0011222222219991",
        "0000111111119991",
        "0000000000011111",
    ];

    private static readonly string[] CatWorking =
    [
        "0000000000000000",
        "0110000000000110",
        "0121000000001210",
        "0123100000013210",
        "0123211111123210",
        "0122222442222210",
        "1222224444222221",
        "1222562222562221",
        "1222662222662221",
        "1442222222222441",
        "1222277887722221",
        "1222277117711111",
        "012222222221AAA1",
        "001122222221AAA1",
        "000011111111AAA1",
        "0000000000011111",
    ];

    private static readonly string[] CatWaiting =
    [
        "0000000000000000",
        "0110000000000110",
        "0121000000001210",
        "0123100000013210",
        "0123211111123210",
        "0122222442222210",
        "1222224444222221",
        "1222562222562221",
        "1222662222662221",
        "1442222222222441",
        "1222277887722221",
        "1222277117711111",
        "012222222221BBB1",
        "001122222221BBB1",
        "000011111111BBB1",
        "0000000000011111",
    ];

    /// <summary>
    /// Derived, not drawn by the designer: the design's sleeping cat (<see cref="CatSleepingPortrait"/>)
    /// is too wide for 16px, so this is the idle face with the eyes shut the same way — the glint
    /// row gone, a single dark lid line — and the lavender sleeping badge.
    /// </summary>
    private static readonly string[] CatSleeping =
    [
        "0000000000000000",
        "0110000000000110",
        "0121000000001210",
        "0123100000013210",
        "0123211111123210",
        "0122222442222210",
        "1222224444222221",
        "1222222222222221",
        "1222662222662221",
        "1442222222222441",
        "1222277887722221",
        "1222277117711111",
        "012222222221CCC1",
        "001122222221CCC1",
        "000011111111CCC1",
        "0000000000011111",
    ];

    /// <summary>
    /// The sitting cat for the waiting card, 18×20, from the design's 108×120 and 180×200 sheets
    /// (the same grid at 6 and 10 pixels per cell). Unlike the dog's portrait it has no bang, so
    /// there is nothing to blink.
    /// </summary>
    public static readonly string[] CatPortrait =
    [
        "000000000000000000",
        "000110000000011000",
        "000121000000121000",
        "000132100001231000",
        "000132211112231000",
        "000122224422221000",
        "001222244442222100",
        "001225622225622100",
        "001226622226622100",
        "001422222222224100",
        "001222778877222100",
        "000122771177221000",
        "000011111111110000",
        "000012227722210010",
        "000012277772210121",
        "000014277772410141",
        "000012277772210121",
        "000122277772221121",
        "000127771177721121",
        "000111111111111111",
    ];

    /// <summary>The tray sprite for a pose, as the dog or the cat, with the palette it indexes.</summary>
    public static (string[] Grid, Color[] Palette) Tray(DogState state, bool cat) => cat
        ? (state switch
        {
            DogState.Working => CatWorking,
            DogState.Waiting => CatWaiting,
            DogState.Idle => CatIdle,
            _ => CatSleeping,
        }, CatPalette)
        : (For(state), TrayPalette);

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
