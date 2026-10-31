import Foundation

/// Maps `VitalsState` onto the dog pose.
///
/// The same rule as `DogStates.For` in the Windows build's Core, which the ESP32 display also
/// follows. The shared fixtures in `fixtures/dog-states.json` hold both implementations to it.
public enum DogStates {
    /// How long `idle` must hold before the dog falls asleep.
    ///
    /// Deliberately shorter than `VitalsState.sessionIdleTimeout`. They answer different
    /// questions: the icon is ambient and can afford to settle after a few quiet minutes, whereas
    /// the session status should not claim a session is over while the user is reading a reply.
    public static let sleepAfter: TimeInterval = 5 * 60

    /// - Parameter now: injected so the transition is testable without waiting five minutes,
    ///   and so the session check and the idle check below judge the same instant.
    public static func pose(for state: VitalsState, at now: Date) -> DogState {
        // A closed or long-dead session outranks whatever the last activity happened to be:
        // Stop fires before SessionEnd, so the final activity of every session is idle.
        switch state.sessionStatus(at: now) {
        case .ended, .inactive, .unknown:
            return .sleeping
        case .active:
            break
        }

        switch state.activity {
        case .working: return .working
        case .waiting: return .waiting
        case .idle: return hasBeenIdleLongEnoughToSleep(state, now) ? .sleeping : .idle
        case .unknown: return .sleeping
        }
    }

    private static func hasBeenIdleLongEnoughToSleep(_ state: VitalsState, _ now: Date) -> Bool {
        // No timestamp means the state was never observed changing, which is not evidence of a
        // recent turn — err towards asleep rather than showing a falsely alert dog forever.
        guard let changed = state.activityChangedUtc else { return true }
        return now.timeIntervalSince(changed) >= sleepAfter
    }
}
