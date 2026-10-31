import BorisCodeStatusCore
import Foundation

/// What the waiting card says, derived from the Notification hook's text.
public struct WaitingPrompt: Equatable, Sendable {
    public static let fallbackDetail = "Claude Code is waiting for your input."

    /// Pixel-font heading, e.g. "Permission needed".
    public var title: String
    /// The hook's own sentence, shown as-is beneath the heading.
    public var detail: String
    /// Short label beside the bar ("Y / N"), or nil for none.
    public var hint: String?

    public init(title: String, detail: String, hint: String?) {
        self.title = title
        self.detail = detail
        self.hint = hint
    }

    /// Classifies by the message text because that is all the hook payload is known to carry: the
    /// Notification event's documented field is `message`. The phrases matched are the ones Claude
    /// Code has been seen to send ("Claude needs your permission to use Bash", "Claude is waiting
    /// for your input"); they are not a contract, so anything unrecognised falls through to a
    /// heading that is true of every Notification rather than a guess. The same rule as Windows'
    /// `WaitingPrompt.From`.
    public static func from(_ message: String?) -> WaitingPrompt {
        let trimmed = message?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        let detail = trimmed.isEmpty ? fallbackDetail : trimmed

        if detail.localizedCaseInsensitiveContains("permission") {
            // The answer is given in the terminal, not on the card — the hint says what Claude is
            // asking for, and the card itself takes no input.
            return WaitingPrompt(title: "Permission needed", detail: detail, hint: "Y / N")
        }

        if detail.localizedCaseInsensitiveContains("waiting for your input") {
            return WaitingPrompt(title: "Input needed", detail: detail, hint: nil)
        }

        return WaitingPrompt(title: "Claude needs you", detail: detail, hint: nil)
    }
}

/// What a `CardPanel` shows. `state` is the header on the right, in `accent`. `dog` nil draws the
/// pet's waiting portrait (the dog's with its blinking bang); a pose draws that menu-bar sprite.
/// `gauge` nil makes the bar the countdown; a percentage makes it the quota gauge.
public struct CardContent: Equatable, Sendable {
    public var state: String
    public var accent: PixelColor
    public var title: String
    public var detail: String
    public var hint: String?
    public var dog: DogState?
    public var gauge: Double?

    public init(state: String, accent: PixelColor, title: String, detail: String, hint: String?, dog: DogState? = nil, gauge: Double? = nil) {
        self.state = state
        self.accent = accent
        self.title = title
        self.detail = detail
        self.hint = hint
        self.dog = dog
        self.gauge = gauge
    }

    public static func from(_ prompt: WaitingPrompt) -> CardContent {
        CardContent(
            state: "Waiting",
            accent: SpriteData.palette[SpriteData.bangIndex],
            title: prompt.title,
            detail: prompt.detail,
            hint: prompt.hint
        )
    }

    /// The accent for a pose: the colour of its menu-bar badge, orange for the badge-less idle face.
    public static func accent(for dog: DogState) -> PixelColor {
        switch dog {
        case .working: SpriteData.trayPalette[5]
        case .waiting: SpriteData.trayPalette[6]
        case .sleeping: SpriteData.trayPalette[7]
        case .idle: SpriteData.trayPalette[3]
        }
    }
}
