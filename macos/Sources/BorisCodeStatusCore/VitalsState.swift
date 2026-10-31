import Foundation

/// Everything the app knows about Claude Code, persisted as `state.json`.
///
/// The Mac build serves no `/status`, but the file keeps exactly the Windows schema — same
/// member names, same order — so both implementations share test fixtures and read each other's
/// files. That includes `week_sonnet`, `month_cost_usd` and `usage_api_last_success_utc`, which
/// nothing on the Mac ever fills: there is no usage-API fallback here (see docs/macos-port.md).
/// They are kept, and preserved when read, rather than dropped from the schema.
public struct VitalsState: Equatable, Sendable {
    /// How long a session may go without any event before it is reported as `inactive`.
    /// Generous on purpose: a gap while the user reads a long reply or steps away is still an
    /// open session, and flapping between active and inactive would be worse than reacting slowly.
    public static let sessionIdleTimeout: TimeInterval = 15 * 60

    /// Five-hour session quota. Nil until a statusLine event has been seen.
    public var session: RateLimitWindow?

    /// Seven-day quota across all models.
    public var week: RateLimitWindow?

    /// Seven-day Sonnet-only quota. Always nil on the Mac, which has no usage-API fallback.
    public var weekSonnet: RateLimitWindow?

    public var contextUsedPercentage: Double?
    public var modelDisplayName: String?
    public var sessionId: String?
    public var sessionName: String?

    /// Cost of the current session only, as reported by statusLine.
    public var sessionCostUsd: Double?
    public var sessionDurationMs: Int64?

    /// Always nil: no Claude Code data source exposes a month figure. See docs/how-it-works.md.
    public var monthCostUsd: Double?

    public var activity: ActivityState = .unknown

    /// When `activity` last changed. Side effects — cards, sounds — key off this so they fire
    /// on a transition rather than on every refresh while a state holds.
    public var activityChangedUtc: Date?

    /// What Claude Code is blocked on, from the Notification hook's own text. Nil unless
    /// `activity` is `waiting`; cleared on the way out so a stale prompt cannot be shown against
    /// a later state.
    public var waitingMessage: String?

    /// When a Claude Code session event (statusLine or a lifecycle hook) was last seen.
    public var lastEventUtc: Date?

    /// When the SessionEnd hook last fired. Persisted so the status survives a restart.
    public var sessionEndedUtc: Date?

    /// When any field was last written. Stamped by `VitalsStateStore`.
    public var lastUpdatedUtc: Date

    /// Always nil on the Mac; see `weekSonnet`.
    public var usageApiLastSuccessUtc: Date?

    public init(lastUpdatedUtc: Date = Date()) {
        self.lastUpdatedUtc = lastUpdatedUtc
    }

    /// Whether a session is open at `now`. Computed rather than stored: a session going quiet
    /// writes nothing to the state file, so a stored value would sit at `active` forever.
    ///
    /// Takes the instant as a parameter so a caller that already has a clock — the dog-pose
    /// rule, and tests — judges the whole decision against one moment.
    public func sessionStatus(at now: Date) -> SessionStatus {
        guard let lastEvent = lastEventUtc else { return .unknown }

        // SessionEnd stamps both timestamps with the same instant, so ">=" is what lets an end
        // win over the event that carried it; any later event moves lastEventUtc past it.
        if let ended = sessionEndedUtc, ended >= lastEvent {
            return .ended
        }

        return now.timeIntervalSince(lastEvent) > VitalsState.sessionIdleTimeout ? .inactive : .active
    }

    /// Seconds since `lastUpdatedUtc` at `now`. Rounds half to even, as .NET does.
    public func ageSeconds(at now: Date) -> Int {
        max(0, Int(now.timeIntervalSince(lastUpdatedUtc).rounded(.toNearestOrEven)))
    }
}

// MARK: - state.json

extension VitalsState: Decodable {
    private enum CodingKeys: String, CodingKey {
        case session
        case week
        case weekSonnet = "week_sonnet"
        case contextUsedPercentage = "context_used_percentage"
        case modelDisplayName = "model_display_name"
        case sessionId = "session_id"
        case sessionName = "session_name"
        case sessionCostUsd = "session_cost_usd"
        case sessionDurationMs = "session_duration_ms"
        case monthCostUsd = "month_cost_usd"
        case activity
        case activityChangedUtc = "activity_changed_utc"
        case waitingMessage = "waiting_message"
        case lastEventUtc = "last_event_utc"
        case sessionEndedUtc = "session_ended_utc"
        case lastUpdatedUtc = "last_updated_utc"
        case usageApiLastSuccessUtc = "usage_api_last_success_utc"
    }

    // The computed members the Windows build also writes (session_status, age_seconds,
    // resets_in_minutes) are ignored on read: they are recomputed from the timestamps.
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        session = try c.decodeIfPresent(RateLimitWindow.self, forKey: .session)
        week = try c.decodeIfPresent(RateLimitWindow.self, forKey: .week)
        weekSonnet = try c.decodeIfPresent(RateLimitWindow.self, forKey: .weekSonnet)
        contextUsedPercentage = try c.decodeIfPresent(Double.self, forKey: .contextUsedPercentage)
        modelDisplayName = try c.decodeIfPresent(String.self, forKey: .modelDisplayName)
        sessionId = try c.decodeIfPresent(String.self, forKey: .sessionId)
        sessionName = try c.decodeIfPresent(String.self, forKey: .sessionName)
        sessionCostUsd = try c.decodeIfPresent(Double.self, forKey: .sessionCostUsd)
        sessionDurationMs = try c.decodeIfPresent(Int64.self, forKey: .sessionDurationMs)
        monthCostUsd = try c.decodeIfPresent(Double.self, forKey: .monthCostUsd)
        activity = try c.decodeIfPresent(ActivityState.self, forKey: .activity) ?? .unknown
        activityChangedUtc = FlexibleDate.decode(c, .activityChangedUtc)
        waitingMessage = try c.decodeIfPresent(String.self, forKey: .waitingMessage)
        lastEventUtc = FlexibleDate.decode(c, .lastEventUtc)
        sessionEndedUtc = FlexibleDate.decode(c, .sessionEndedUtc)
        lastUpdatedUtc = FlexibleDate.decode(c, .lastUpdatedUtc) ?? Date(timeIntervalSince1970: 0)
        usageApiLastSuccessUtc = FlexibleDate.decode(c, .usageApiLastSuccessUtc)
    }

    /// The `state.json` document, in the Windows build's member order. The computed members are
    /// written as of `now`, as the Windows build does; nothing reads them back.
    public func json(at now: Date) -> JSONValue {
        func date(_ value: Date?) -> JSONValue { .string(value.map(FlexibleDate.format)) }
        func double(_ value: Double?) -> JSONValue { value.map { .number($0) } ?? .null }
        func window(_ value: RateLimitWindow?) -> JSONValue {
            guard let value else { return .null }
            return .object([
                ("used_percentage", double(value.usedPercentage)),
                ("resets_at", date(value.resetsAt)),
                ("resets_in_minutes", value.resetsInMinutes(at: now).map { .number(Int64($0)) } ?? .null),
            ])
        }

        return .object([
            ("session", window(session)),
            ("week", window(week)),
            ("week_sonnet", window(weekSonnet)),
            ("context_used_percentage", double(contextUsedPercentage)),
            ("model_display_name", .string(modelDisplayName)),
            ("session_id", .string(sessionId)),
            ("session_name", .string(sessionName)),
            ("session_cost_usd", double(sessionCostUsd)),
            ("session_duration_ms", sessionDurationMs.map { .number($0) } ?? .null),
            ("month_cost_usd", double(monthCostUsd)),
            ("activity", .string(activity.rawValue)),
            ("activity_changed_utc", date(activityChangedUtc)),
            ("waiting_message", .string(waitingMessage)),
            ("last_event_utc", date(lastEventUtc)),
            ("session_ended_utc", date(sessionEndedUtc)),
            ("session_status", .string(sessionStatus(at: now).rawValue)),
            ("last_updated_utc", date(lastUpdatedUtc)),
            ("usage_api_last_success_utc", date(usageApiLastSuccessUtc)),
            ("age_seconds", .number(Int64(ageSeconds(at: now)))),
        ])
    }
}
