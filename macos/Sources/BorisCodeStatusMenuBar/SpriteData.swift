// Generated from BorisCodeStatus.Tray/DogSprites.cs — the same grids and palettes, transcribed by
// script rather than by hand so no pixel can drift. Edit both files together.

/// The pets, as palette-index grids rather than image files, exactly as the Windows build keeps
/// them: one character per pixel, '0' transparent through 'F' = 15. Kept as data in source so
/// there are no image files in the repo and the glyph stays diffable. The selectors are in
/// `PetSprites.swift`.
enum SpriteData {
    /// The notification card's eleven-colour palette, in the original design's order. Indices 4 and 5
    /// are the shade ramp; the rest is kept as a faithful copy rather than a subset that silently
    /// renumbers everything after it.
    static let palette: [PixelColor] =
    [
        PixelColor.clear,              // 0
        PixelColor(0x17, 0x15, 0x0F), // 1 outline / ink
        PixelColor(0xF2, 0xED, 0xE4), // 2 cream fur
        PixelColor(0xD9, 0x77, 0x57), // 3 orange patch
        PixelColor(0xA6, 0x54, 0x3A), // 4 orange shade
        PixelColor(0xCF, 0xC7, 0xB8), // 5 fur shade
        PixelColor(0xE5, 0x8F, 0xA0), // 6 tongue
        PixelColor(0xFF, 0xFF, 0xFF), // 7 eye glint
        PixelColor(0x6F, 0xA9, 0x6A), // 8 idle green
        PixelColor(0xE8, 0xB0, 0x4B), // 9 waiting amber
        PixelColor(0x7A, 0x8A, 0xA3), // 10 sleeping slate
    ]

    /// The tray icons' palette, sampled exactly from the design's PNGs.
    static let trayPalette: [PixelColor] =
    [
        PixelColor.clear,                // 0
        PixelColor(0x2A, 0x1F, 0x1A), // 1 outline
        PixelColor(0xF4, 0xEE, 0xE2), // 2 cream fur
        PixelColor(0xE0, 0x72, 0x58), // 3 orange ears and patch
        PixelColor(0x11, 0x11, 0x11), // 4 eyes and nose
        PixelColor(0x6C, 0xC6, 0xF0), // 5 working badge, blue
        PixelColor(0xF5, 0xB9, 0x42), // 6 waiting badge, amber
        PixelColor(0xC8, 0xC8, 0xD7), // 7 sleeping badge, lavender
    ]

    /// Blue square badge.
    static let working: [String] =
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
    ]

    /// No badge — the plain face.
    static let idle: [String] =
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
    ]

    /// Amber "!" badge — the pose that should catch the eye.
    static let waiting: [String] =
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
    ]

    /// Eyes shut, lavender "Z" badge.
    static let sleeping: [String] =
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
    ]

    /// The full-size waiting dog for the notification card — the 32×32 design sprite trimmed to
    /// its bounds (23×26), sitting up with the "!" bang. Sampled from the design's rendered card,
    /// where every sprite pixel is a clean 4×4 block of an exact palette colour, so this is the
    /// design's grid rather than a redrawing of it.
    /// Not tied to the tray size above: the card has room for the full portrait.
    /// It is still drawn at an integer pixel size only. Index 9 appears nowhere but the bang, which
    /// is what lets the card blink the bang alone by skipping that index.
    static let waitingPortrait: [String] =
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
    ]

    /// The palette index used only by the bang in `waitingPortrait`.
    static let bangIndex: Int = 9
    /// The cat's palette, sampled exactly from the design's PNGs. Unlike the dog, idle has a badge
    /// of its own (green); the sleeping badge reuses the dog's lavender.
    static let catPalette: [PixelColor] =
    [
        PixelColor.clear,                // 0
        PixelColor(0x3B, 0x23, 0x14), // 1 outline
        PixelColor(0xF2, 0x8C, 0x28), // 2 orange fur
        PixelColor(0xF2, 0xA1, 0xA1), // 3 inner ear pink
        PixelColor(0xC4, 0x5F, 0x12), // 4 dark orange stripes
        PixelColor(0xFF, 0xFF, 0xFF), // 5 eye glint
        PixelColor(0x1A, 0x12, 0x09), // 6 eyes
        PixelColor(0xFF, 0xF1, 0xD6), // 7 cream muzzle and chest
        PixelColor(0xE0, 0x70, 0x7A), // 8 nose
        PixelColor(0x4C, 0xC2, 0x6A), // 9 idle badge, green
        PixelColor(0x4F, 0xA8, 0xE6), // A working badge, blue
        PixelColor(0xFF, 0xD2, 0x3F), // B waiting badge, yellow
        PixelColor(0xC8, 0xC8, 0xD7), // C sleeping badge, lavender
        PixelColor(0xA0, 0xC8, 0xEB), // D the sleeping cat's Z's
    ]

    /// The curled-up sleeping cat with its Z's, 22×14, from the design's 132×84 and 220×140
    /// sheets. For the status card only: it is wider than the 16px tray icon, so the tray keeps
    /// `catSleeping`.
    static let catSleepingPortrait: [String] =
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
    ]

    static let catIdle: [String] =
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
    ]

    static let catWorking: [String] =
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
    ]

    static let catWaiting: [String] =
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
    ]

    /// Derived, not drawn by the designer: the design's sleeping cat (`catSleepingPortrait`)
    /// is too wide for 16px, so this is the idle face with the eyes shut the same way — the glint
    /// row gone, a single dark lid line — and the lavender sleeping badge.
    static let catSleeping: [String] =
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
    ]

    /// The sitting cat for the waiting card, 18×20, from the design's 108×120 and 180×200 sheets
    /// (the same grid at 6 and 10 pixels per cell). Unlike the dog's portrait it has no bang, so
    /// there is nothing to blink.
    static let catPortrait: [String] =
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
    ]

    /// The sentry bot's palette, sampled exactly from the design's PNGs. Its eye and antenna tip are
    /// the badge: unlit red at idle, lit red working, amber waiting.
    static let botPalette: [PixelColor] =
    [
        PixelColor.clear,                // 0
        PixelColor(0x70, 0x1E, 0x1E), // 1 unlit eye and antenna
        PixelColor(0x24, 0x2C, 0x36), // 2 outline
        PixelColor(0xC3, 0xCC, 0xD6), // 3 light steel
        PixelColor(0x8A, 0x97, 0xA6), // 4 steel
        PixelColor(0x12, 0x16, 0x1B), // 5 visor
        PixelColor(0x4B, 0x56, 0x63), // 6 dark steel
        PixelColor(0xFF, 0x3B, 0x30), // 7 working, red
        PixelColor(0xFF, 0xB0, 0xA0), // 8 red glint
        PixelColor(0xFF, 0xB0, 0x2E), // 9 waiting, amber
        PixelColor(0xFF, 0xE6, 0x96), // A amber glint
    ]

    static let botIdle: [String] =
    [
        "0000000110000000",
        "0000000220000000",
        "0022222222222200",
        "0023333333333200",
        "0024444444444200",
        "0025555555555200",
        "0025511111155200",
        "0025555555555200",
        "0025555555555200",
        "0024444444444200",
        "0024646446464200",
        "0022222222222200",
        "0000026666200000",
        "0222222222222220",
        "0243344444433420",
        "0222222222222220",
    ]

    static let botWorking: [String] =
    [
        "0000000770000000",
        "0000000220000000",
        "0022222222222200",
        "0023333333333200",
        "0024444444444200",
        "0025555555555200",
        "0025777887775200",
        "0025777777775200",
        "0025555555555200",
        "0024444444444200",
        "0024646446464200",
        "0022222222222200",
        "0000026666200000",
        "0222222222222220",
        "0243344444433420",
        "0222222222222220",
    ]

    static let botWaiting: [String] =
    [
        "0000000990000000",
        "0000000220000000",
        "0022222222222200",
        "0023333333333200",
        "0024444444444200",
        "0025555555555200",
        "0025999AA9995200",
        "0025999999995200",
        "0025555555555200",
        "0024444444444200",
        "0024646446464200",
        "0022222222222200",
        "0000026666200000",
        "0222222222222220",
        "0243344444433420",
        "0222222222222220",
    ]

    /// Derived, not drawn by the designer: the idle bot with its eye switched off.
    static let botSleeping: [String] =
    [
        "0000000110000000",
        "0000000220000000",
        "0022222222222200",
        "0023333333333200",
        "0024444444444200",
        "0025555555555200",
        "0025555555555200",
        "0025555555555200",
        "0025555555555200",
        "0024444444444200",
        "0024646446464200",
        "0022222222222200",
        "0000026666200000",
        "0222222222222220",
        "0243344444433420",
        "0222222222222220",
    ]

    /// The bot waving for the waiting card, 18×20, from the design's 108×120 sheet.
    static let botPortrait: [String] =
    [
        "000000099000000000",
        "000000022000000000",
        "000222222222222000",
        "000233333333332000",
        "000244444444442000",
        "000255555555552000",
        "00025999AA99952000",
        "000259999999952222",
        "000255555555552232",
        "000244444444442232",
        "000246464464642242",
        "000222222222222242",
        "000000026620000242",
        "002222222222222220",
        "242344444444443200",
        "242466666666664200",
        "242466669966664200",
        "242466666666664200",
        "242444444444444200",
        "222222222222222200",
    ]

    /// The rubber duck's palette, sampled exactly from the design's PNGs, plus the dog's lavender.
    static let duckPalette: [PixelColor] =
    [
        PixelColor.clear,                // 0
        PixelColor(0x6E, 0x40, 0x06), // 1 outline
        PixelColor(0xFF, 0xD0, 0x00), // 2 yellow
        PixelColor(0xFF, 0xF2, 0x8C), // 3 highlight
        PixelColor(0x1E, 0x14, 0x0A), // 4 eye
        PixelColor(0xFF, 0x78, 0x18), // 5 beak
        PixelColor(0xE8, 0x96, 0x00), // 6 wing
        PixelColor(0x4C, 0xC2, 0x6A), // 7 idle badge, green
        PixelColor(0x4F, 0xA8, 0xE6), // 8 working badge and water, blue
        PixelColor(0xFF, 0xD2, 0x3F), // 9 waiting badge, yellow
        PixelColor(0xA0, 0xC8, 0xEB), // A Z's (unused here)
        PixelColor(0xFF, 0x8C, 0x6E), // B cheek
        PixelColor(0x2E, 0x6E, 0xA0), // C deep water
        PixelColor(0xFF, 0xB0, 0x2E), // D the "!" sign, amber
        PixelColor(0xFF, 0xFF, 0xFF), // E eye glint
        PixelColor(0xC8, 0xC8, 0xD7), // F sleeping badge, lavender
    ]

    static let duckIdle: [String] =
    [
        "0000000000000000",
        "0000111111000000",
        "0001222222100000",
        "0001232222100000",
        "0001242222100000",
        "0111222222100110",
        "1551222222101210",
        "0111222222222210",
        "0012222222222210",
        "0122226666222210",
        "0122226336222210",
        "0122226666211111",
        "0012222222217771",
        "0001622222217771",
        "0000111111117771",
        "0088888888811111",
    ]

    static let duckWorking: [String] =
    [
        "0000000000000000",
        "0000111111000000",
        "0001222222100000",
        "0001232222100000",
        "0001242222100000",
        "0111222222100110",
        "1551222222101210",
        "0111222222222210",
        "0012222222222210",
        "0122226666222210",
        "0122226336222210",
        "0122226666211111",
        "0012222222218881",
        "0001622222218881",
        "0000111111118881",
        "0088888888811111",
    ]

    static let duckWaiting: [String] =
    [
        "0000000000000000",
        "0000111111000000",
        "0001222222100000",
        "0001232222100000",
        "0001242222100000",
        "0111222222100110",
        "1551222222101210",
        "0111222222222210",
        "0012222222222210",
        "0122226666222210",
        "0122226336222210",
        "0122226666211111",
        "0012222222219991",
        "0001622222219991",
        "0000111111119991",
        "0088888888811111",
    ]

    /// Derived, not drawn by the designer: the idle duck with its eye shut and the lavender badge.
    static let duckSleeping: [String] =
    [
        "0000000000000000",
        "0000111111000000",
        "0001222222100000",
        "0001232222100000",
        "0001212222100000",
        "0111222222100110",
        "1551222222101210",
        "0111222222222210",
        "0012222222222210",
        "0122226666222210",
        "0122226336222210",
        "0122226666211111",
        "001222222221FFF1",
        "000162222221FFF1",
        "000011111111FFF1",
        "0088888888811111",
    ]

    /// The duck holding up its "!" sign for the waiting card, 18×23, from the design's 108×138 sheet.
    static let duckPortrait: [String] =
    [
        "000000000000111110",
        "000000000001D444D1",
        "000000000001DDD4D1",
        "000000000001DD4DD1",
        "000000000001DDDDD1",
        "000001111111DD4DD1",
        "000012222221111110",
        "000123322222100000",
        "000122222222100000",
        "00012E422222100011",
        "011124422222100121",
        "1551B2222222101221",
        "155122222222101221",
        "011122222222222221",
        "001222222222222221",
        "012222226666622221",
        "012222263336222221",
        "012222226666622221",
        "001222222222222221",
        "000162222222222610",
        "000011111111111100",
        "008888888888888800",
        "0000CCCCCCCCCC0000",
    ]

    /// The goat's palette. Unlike the other pets it was not sampled from the designer's sheets — there
    /// are none — so the colours are chosen to sit with the cat's and duck's: the same outline weight
    /// and the same four badge colours (index 9, A, B and C), so the tray's badge accent and the
    /// card read the same whoever is in the tray.
    static let goatPalette: [PixelColor] =
    [
        PixelColor.clear,                // 0
        PixelColor(0x3B, 0x2A, 0x1E), // 1 outline
        PixelColor(0xF4, 0xEF, 0xE6), // 2 white coat
        PixelColor(0xE8, 0xA0, 0xA0), // 3 inner ear pink
        PixelColor(0xD2, 0xB0, 0x72), // 4 horn
        PixelColor(0xFF, 0xFF, 0xFF), // 5 eye glint
        PixelColor(0x1A, 0x12, 0x09), // 6 eye
        PixelColor(0x7A, 0x1F, 0x2B), // 7 open mouth
        PixelColor(0xE5, 0x60, 0x7A), // 8 tongue
        PixelColor(0x4C, 0xC2, 0x6A), // 9 idle badge, green
        PixelColor(0x4F, 0xA8, 0xE6), // A working badge, blue
        PixelColor(0xFF, 0xD2, 0x3F), // B waiting badge, yellow
        PixelColor(0xC8, 0xC8, 0xD7), // C sleeping badge, lavender
        PixelColor(0xA0, 0xC8, 0xEB), // D (unused)
        PixelColor(0xBF, 0xB6, 0xA4), // E (unused)
        PixelColor(0x8A, 0x6A, 0x5A), // F nostril
    ]

    // The goat's mouth is open and its tongue wags while working or waiting. Each pose is two grids,
    // indexed by `PetSprites.wagFrame`; they differ only in the tongue (and the beard, which swings to the other
    // side), so the horns, face and badge hold still while the tongue moves.

    /// Idle, the goat closes its mouth — a line across the muzzle, the beard hanging straight — and
    /// holds still: nothing is happening, so nothing moves. Two equal entries, as for sleeping.
    static let goatIdleFace: [String] =
    [
        "0011000000001100",
        "0014100000014100",
        "0001410000141000",
        "0000141001410000",
        "0000112222110000",
        "0111222222221110",
        "1333252222523331",
        "0111262222621110",
        "0001222222221000",
        "0000122FF2210000",
        "0000122222210000",
        "0000122112211111",
        "0000011111119991",
        "0000001221019991",
        "0000000110019991",
        "0000000000011111",
    ]

    static let goatIdle: [[String]] = [goatIdleFace, goatIdleFace]

    static let goatWorking: [[String]] =
    [
        [
            "0011000000001100",
            "0014100000014100",
            "0001410000141000",
            "0000141001410000",
            "0000112222110000",
            "0111222222221110",
            "1333252222523331",
            "0111262222621110",
            "0001222222221000",
            "0000122FF2210000",
            "0000177777710000",
            "0000178877711111",
            "000018811111AAA1",
            "000188122101AAA1",
            "000011011001AAA1",
            "0000000000011111",
        ],
        [
            "0011000000001100",
            "0014100000014100",
            "0001410000141000",
            "0000141001410000",
            "0000112222110000",
            "0111222222221110",
            "1333252222523331",
            "0111262222621110",
            "0001222222221000",
            "0000122FF2210000",
            "0000177777710000",
            "0000177887711111",
            "000011118811AAA1",
            "000012211881AAA1",
            "000001100111AAA1",
            "0000000000011111",
        ],
    ]

    static let goatWaiting: [[String]] =
    [
        [
            "0011000000001100",
            "0014100000014100",
            "0001410000141000",
            "0000141001410000",
            "0000112222110000",
            "0111222222221110",
            "1333252222523331",
            "0111262222621110",
            "0001222222221000",
            "0000122FF2210000",
            "0000177777710000",
            "0000178877711111",
            "000018811111BBB1",
            "000188122101BBB1",
            "000011011001BBB1",
            "0000000000011111",
        ],
        [
            "0011000000001100",
            "0014100000014100",
            "0001410000141000",
            "0000141001410000",
            "0000112222110000",
            "0111222222221110",
            "1333252222523331",
            "0111262222621110",
            "0001222222221000",
            "0000122FF2210000",
            "0000177777710000",
            "0000177887711111",
            "000011118811BBB1",
            "000012211881BBB1",
            "000001100111BBB1",
            "0000000000011111",
        ],
    ]

    /// Derived, like the cat's: the idle face with the glints gone so the eyes read as shut, the
    /// lavender badge, and the tongue left hanging. Both entries are the same grid — a sleeping goat
    /// does not wag, and keeping two entries lets `Tray` index every pose alike.
    static let goatSleeping: [[String]] =
    [
        [
            "0011000000001100",
            "0014100000014100",
            "0001410000141000",
            "0000141001410000",
            "0000112222110000",
            "0111222222221110",
            "1333222222223331",
            "0111262222621110",
            "0001222222221000",
            "0000122FF2210000",
            "0000177777710000",
            "0000178877711111",
            "000018811111CCC1",
            "000188122101CCC1",
            "000011011001CCC1",
            "0000000000011111",
        ],
        [
            "0011000000001100",
            "0014100000014100",
            "0001410000141000",
            "0000141001410000",
            "0000112222110000",
            "0111222222221110",
            "1333222222223331",
            "0111262222621110",
            "0001222222221000",
            "0000122FF2210000",
            "0000177777710000",
            "0000178877711111",
            "000018811111CCC1",
            "000188122101CCC1",
            "000011011001CCC1",
            "0000000000011111",
        ],
    ]

    /// The goat's head for the waiting card, 18×16, in its two wag frames: the tray sprite without its
    /// badge (the card has its own header), padded by a column each side to the 18-wide footprint
    /// the cat and bot portraits use, so the card centres it the same way. Drawn at five pixels a cell.
    static let goatPortrait: [[String]] =
    [
        [
            "000110000000011000",
            "000141000000141000",
            "000014100001410000",
            "000001410014100000",
            "000001122221100000",
            "001112222222211100",
            "013332522225233310",
            "001112622226211100",
            "000012222222210000",
            "00000122FF22100000",
            "000001777777100000",
            "000001788777100000",
            "000001881111000000",
            "000018812210000000",
            "000001101100000000",
            "000000000000000000",
        ],
        [
            "000110000000011000",
            "000141000000141000",
            "000014100001410000",
            "000001410014100000",
            "000001122221100000",
            "001112222222211100",
            "013332522225233310",
            "001112622226211100",
            "000012222222210000",
            "00000122FF22100000",
            "000001777777100000",
            "000001778877100000",
            "000001111881000000",
            "000001221188100000",
            "000000110011000000",
            "000000000000000000",
        ],
    ]
}
