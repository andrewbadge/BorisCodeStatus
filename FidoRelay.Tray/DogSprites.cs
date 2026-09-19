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
