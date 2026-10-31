import Foundation

/// What the Claude Code session is currently doing, derived from lifecycle hooks
/// (not from statusLine, which carries no lifecycle signal).
///
/// Raw values are the names the Windows build writes to `state.json`, so both implementations
/// read each other's files and share test fixtures.
public enum ActivityState: String, Codable, Sendable {
    /// No lifecycle hook has fired yet in this install.
    case unknown = "Unknown"

    /// A turn finished (Stop hook).
    case idle = "Idle"

    /// A prompt was submitted or a tool is about to run (UserPromptSubmit / PreToolUse).
    case working = "Working"

    /// Claude Code is blocked on the user, e.g. a permission prompt (Notification hook).
    case waiting = "Waiting"

    /// An unrecognised value reads as `unknown` rather than failing the whole state file: the
    /// Windows build throws here and falls back to the previous state, which on a fresh start
    /// means no state at all.
    public init(from decoder: Decoder) throws {
        let raw = try decoder.singleValueContainer().decode(String.self)
        self = ActivityState(rawValue: raw) ?? .unknown
    }
}

/// Whether a Claude Code session is open at all, independent of what it is doing right now.
///
/// This exists because `ActivityState` answers a narrower question than it looks like it does:
/// `idle` is set by the Stop hook the moment a turn finishes, so a session the user is actively
/// chatting in reads as `idle` for most of its wall-clock life — all the time spent reading a
/// reply and typing the next prompt.
public enum SessionStatus: String, Codable, Sendable {
    /// No session event has been seen yet in this install.
    case unknown = "Unknown"

    /// A session is open: an event arrived recently and no SessionEnd has followed it.
    case active = "Active"

    /// No session event for longer than the idle timeout. Inferred, not observed — it is what a
    /// session that died without firing SessionEnd (killed terminal, crash) decays into.
    case inactive = "Inactive"

    /// The SessionEnd hook fired — the session closed cleanly.
    case ended = "Ended"
}

/// Which of the four 8-bit dog poses represents the session right now. See `DogStates`.
public enum DogState: String, Codable, Sendable {
    /// Curled up. No session, or an idle one that has gone quiet.
    case sleeping = "Sleeping"

    /// Sitting, alert. A turn has finished and Claude is waiting on nothing.
    case idle = "Idle"

    /// Running. Claude is generating or running a tool.
    case working = "Working"

    /// Ears up, amber. Claude is blocked on the user, e.g. a permission prompt.
    case waiting = "Waiting"
}

/// One quota window (five-hour session or seven-day week) as reported by statusLine.
public struct RateLimitWindow: Equatable, Sendable {
    public var usedPercentage: Double?
    public var resetsAt: Date?

    public init(usedPercentage: Double? = nil, resetsAt: Date? = nil) {
        self.usedPercentage = usedPercentage
        self.resetsAt = resetsAt
    }

    /// Whole minutes until this window resets at `now`, or nil if unknown. Computed rather than
    /// stored, because nothing rewrites the state file while a session is quiet.
    ///
    /// Rounds half to even to match .NET's `Math.Round`, so both builds agree on the minute.
    public func resetsInMinutes(at now: Date) -> Int? {
        guard let resetsAt else { return nil }
        return max(0, Int((resetsAt.timeIntervalSince(now) / 60).rounded(.toNearestOrEven)))
    }
}

extension RateLimitWindow: Decodable {
    private enum CodingKeys: String, CodingKey {
        case usedPercentage = "used_percentage"
        case resetsAt = "resets_at"
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        usedPercentage = try container.decodeIfPresent(Double.self, forKey: .usedPercentage)
        resetsAt = FlexibleDate.decode(container, .resetsAt)
    }
}

/// The JSON Claude Code pipes to a configured `statusLine` command on stdin. Every member is
/// optional: Claude Code omits sections that do not apply (`rate_limits` is absent on API-key
/// sessions), and unknown members are ignored.
public struct StatusLineEvent: Decodable, Sendable {
    public struct Model: Decodable, Sendable {
        public var displayName: String?
        public var id: String?

        private enum CodingKeys: String, CodingKey {
            case displayName = "display_name"
            case id
        }
    }

    public struct Cost: Decodable, Sendable {
        public var totalCostUsd: Double?
        public var totalDurationMs: Int64?

        private enum CodingKeys: String, CodingKey {
            case totalCostUsd = "total_cost_usd"
            case totalDurationMs = "total_duration_ms"
        }
    }

    public struct ContextWindow: Decodable, Sendable {
        public var usedPercentage: Double?

        private enum CodingKeys: String, CodingKey {
            case usedPercentage = "used_percentage"
        }
    }

    public struct RateLimits: Decodable, Sendable {
        public var fiveHour: RateLimitWindow?
        public var sevenDay: RateLimitWindow?

        private enum CodingKeys: String, CodingKey {
            case fiveHour = "five_hour"
            case sevenDay = "seven_day"
        }
    }

    public var sessionId: String?
    public var sessionName: String?
    public var model: Model?
    public var cost: Cost?
    public var contextWindow: ContextWindow?
    public var rateLimits: RateLimits?

    private enum CodingKeys: String, CodingKey {
        case sessionId = "session_id"
        case sessionName = "session_name"
        case model
        case cost
        case contextWindow = "context_window"
        case rateLimits = "rate_limits"
    }
}

/// The JSON Claude Code pipes to a lifecycle hook command on stdin (Notification, Stop,
/// PreToolUse, UserPromptSubmit, SessionStart, SessionEnd). Only the fields this app uses are
/// modelled; the rest of the payload — including the prompt text on UserPromptSubmit — is
/// discarded unread. docs/privacy.md promises exactly this list.
public struct HookEvent: Decodable, Sendable {
    public var sessionId: String?
    public var hookEventName: String?

    /// Set on PreToolUse/PostToolUse.
    public var toolName: String?

    /// Set on Notification — the text shown to the user, e.g. a permission request.
    public var message: String?

    public init(sessionId: String? = nil, hookEventName: String? = nil, toolName: String? = nil, message: String? = nil) {
        self.sessionId = sessionId
        self.hookEventName = hookEventName
        self.toolName = toolName
        self.message = message
    }

    private enum CodingKeys: String, CodingKey {
        case sessionId = "session_id"
        case hookEventName = "hook_event_name"
        case toolName = "tool_name"
        case message
    }
}
