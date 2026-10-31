import Foundation

/// Pure mapping from a parsed Claude Code payload onto `VitalsState`. Kept separate from
/// `VitalsStateStore` so the mapping can be tested without touching the filesystem.
///
/// Unlike the Windows build, every fold takes its instant as a parameter rather than reading
/// the clock itself, so a test can pin the timestamps it produces.
public enum VitalsUpdates {
    /// Parses a payload Claude Code piped to stdin. Returns nil for empty or malformed input
    /// rather than throwing — a hook that fails is worse than a hook that does nothing.
    public static func parse<T: Decodable>(_ type: T.Type, from json: String?) -> T? {
        guard let json, !json.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return nil }
        return try? JSONDecoder().decode(type, from: Data(json.utf8))
    }

    /// Folds a statusLine payload into the state. Fields absent from the payload keep their
    /// previous value: Claude Code omits `rate_limits` on API-key sessions, and blanking the
    /// display in that case would be worse than showing slightly stale numbers.
    public static func applyStatusLine(_ current: VitalsState, _ status: StatusLineEvent, now: Date) -> VitalsState {
        var next = current
        next.session = status.rateLimits?.fiveHour ?? current.session
        next.week = status.rateLimits?.sevenDay ?? current.week
        next.contextUsedPercentage = status.contextWindow?.usedPercentage ?? current.contextUsedPercentage
        next.modelDisplayName = coalesce(status.model?.displayName, current.modelDisplayName)
        next.sessionId = coalesce(status.sessionId, current.sessionId)
        next.sessionName = coalesce(status.sessionName, current.sessionName)
        next.sessionCostUsd = status.cost?.totalCostUsd ?? current.sessionCostUsd
        next.sessionDurationMs = status.cost?.totalDurationMs ?? current.sessionDurationMs
        next.lastEventUtc = now
        return next
    }

    /// Folds a lifecycle hook payload into the state. `activityChangedUtc` only moves when the
    /// activity actually changes, so the display can show how long a state has held.
    public static func applyActivity(_ current: VitalsState, _ activity: ActivityState, hook: HookEvent? = nil, now: Date) -> VitalsState {
        var next = current
        next.activity = activity
        next.activityChangedUtc = current.activity == activity ? (current.activityChangedUtc ?? now) : now
        next.sessionId = coalesce(hook?.sessionId, current.sessionId)
        next.lastEventUtc = now

        // Only waiting carries a message, and only its own: leaving the state clears it, so a
        // permission prompt from ten minutes ago can never be shown against a later state.
        next.waitingMessage = activity == .waiting ? coalesce(hook?.message, current.waitingMessage) : nil
        return next
    }

    /// Folds a SessionStart or SessionEnd hook into the state. Deliberately leaves `activity`
    /// alone: session lifetime and turn activity are separate signals.
    public static func applySessionLifetime(_ current: VitalsState, ended: Bool, hook: HookEvent? = nil, now: Date) -> VitalsState {
        var next = current
        next.sessionId = coalesce(hook?.sessionId, current.sessionId)
        // One instant for both stamps: sessionStatus compares them.
        next.lastEventUtc = now
        // A start clears any previous end, so a new session is not reported as ended.
        next.sessionEndedUtc = ended ? now : nil
        return next
    }

    /// Maps the command-line verb each hook registration passes to the activity it means.
    public static func activity(forVerb verb: String?) -> ActivityState? {
        switch normalised(verb) {
        case "notification": .waiting
        case "stop": .idle
        case "pretooluse", "userpromptsubmit", "working": .working
        default: nil
        }
    }

    /// Maps a verb to a session-lifetime change: true for an end, false for a start, nil when the
    /// verb is not a session-lifetime hook at all.
    public static func sessionLifetime(forVerb verb: String?) -> Bool? {
        switch normalised(verb) {
        case "sessionstart": false
        case "sessionend": true
        default: nil
        }
    }

    private static func normalised(_ verb: String?) -> String? {
        verb?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
    }

    private static func coalesce(_ incoming: String?, _ existing: String?) -> String? {
        guard let incoming, !incoming.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { return existing }
        return incoming
    }
}
