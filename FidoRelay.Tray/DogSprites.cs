using System.Drawing;
using FidoRelay.Core.Models;

namespace FidoRelay.Tray;

/// <summary>
/// The 8-bit dog, as palette-index grids rather than image files.
///
/// Transcribed pixel-for-pixel from the design's 20px tray sheets. Kept as data in source for the
/// same reason the icon was drawn at runtime before: no binaries in the repo, nothing to keep in
/// sync with a build step, and the glyph stays greppable and diffable. 20px is the largest size
/// that clears the quota ring on a 32px canvas without the ring cutting into the dog.
///
/// Each character is an index into <see cref="Palette"/>: '0' transparent through 'A' = 10.
/// </summary>
internal static class DogSprites
{
    /// <summary>
    /// The design's eleven-colour palette, in its original order. Indices 4 and 5 are the shade
    /// ramp, which the larger sprites use but the 20px ones do not; they are kept so the palette
    /// stays a faithful copy rather than a subset that silently renumbers everything after it.
    /// </summary>
    public static readonly Color[] Palette =
    [
        Color.Transparent,              // 0
        Color.FromArgb(0x17, 0x15, 0x0F), // 1 outline / ink
        Color.FromArgb(0xF2, 0xED, 0xE4), // 2 cream fur
        Color.FromArgb(0xD9, 0x77, 0x57), // 3 orange patch — also the Working accent
        Color.FromArgb(0xA6, 0x54, 0x3A), // 4 orange shade
        Color.FromArgb(0xCF, 0xC7, 0xB8), // 5 fur shade
        Color.FromArgb(0xE5, 0x8F, 0xA0), // 6 tongue
        Color.FromArgb(0xFF, 0xFF, 0xFF), // 7 eye glint
        Color.FromArgb(0x6F, 0xA9, 0x6A), // 8 idle green
        Color.FromArgb(0xE8, 0xB0, 0x4B), // 9 waiting amber
        Color.FromArgb(0x7A, 0x8A, 0xA3), // 10 sleeping slate
    ];

    public const int Size = 20;

    /// <summary>Running, tongue out, orange accent.</summary>
    private static readonly string[] Working =
    [
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "01111111111111111100",
        "13333222223333333310",
        "13333222223333333310",
        "13333222223333333310",
        "13337122223771333310",
        "13331122223111333310",
        "13333222222222333310",
        "01122222112222221100",
        "01122222112222221100",
        "01122222112222221100",
        "00012222222221111110",
        "00001222662221333310",
        "00000111111111333310",
        "00000111111111333310",
        "00000000000001333310",
        "00000000000001111110",
    ];

    /// <summary>Sitting, calm, green accent.</summary>
    private static readonly string[] Idle =
    [
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "01111111111111111100",
        "13333222223333333310",
        "13333222223333333310",
        "13333222223333333310",
        "13337122223771333310",
        "13331122223111333310",
        "13333222222222333310",
        "01122222112222221100",
        "01122222112222221100",
        "01122222112222221100",
        "00012222222221111110",
        "00001222222221888810",
        "00000111111111888810",
        "00000111111111888810",
        "00000000000001888810",
        "00000000000001111110",
    ];

    /// <summary>Ears pricked up, amber accent — the pose that should catch the eye.</summary>
    private static readonly string[] Waiting =
    [
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "01111000000000111100",
        "13333111111111333310",
        "13333222223333333310",
        "13333222223333333310",
        "13333222223333333310",
        "13337122223771333310",
        "13331122223111333310",
        "01121122222111221100",
        "01122222112222221100",
        "01122222112222221100",
        "01122222112222221100",
        "00012222222221111110",
        "00001222222221999910",
        "00000111111111999910",
        "00000111111111999910",
        "00000000000001999910",
        "00000000000001111110",
    ];

    /// <summary>Curled up, eyes closed, slate accent.</summary>
    private static readonly string[] Sleeping =
    [
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "00000000000000000000",
        "00000111111111000000",
        "01111222223333111100",
        "13333222223333333310",
        "13333222223333333310",
        "13333222223333333310",
        "13331111223111133310",
        "13333222222222333310",
        "13333222112222333310",
        "13333222112222333310",
        "01122222112222221100",
        "00012222222221111110",
        "00001222222221AAAA10",
        "00000111111111AAAA10",
        "00000111111111AAAA10",
        "00000000000001AAAA10",
        "00000000000001111110",
    ];

    /// <summary>
    /// The full-size waiting dog for the notification card — the 32×32 design sprite trimmed to
    /// its bounds (23×26), sitting up with the "!" bang. Sampled from the design's rendered card,
    /// where every sprite pixel is a clean 4×4 block of an exact palette colour, so this is the
    /// design's grid rather than a redrawing of it.
    ///
    /// Not bound by the 20px tray limit above: that limit is the tray ring, and the card has room.
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
