import Foundation
import Testing
@testable import BorisCodeStatusCore

/// Runs the cases in the repo-root `fixtures/` folder, which the Windows build's C# tests run too
/// (`SharedFixtureTests.cs`). Two implementations of the pose rule and the hook folds can drift
/// apart; holding both to the same data is what stops that.
struct SharedFixtureTests {
    /// SwiftPM cannot bundle resources from outside the package, so find the folder relative to
    /// this source file: macos/Tests/BorisCodeStatusCoreTests/ is three levels below the repo root.
    static let fixtures = URL(filePath: #filePath)
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .deletingLastPathComponent()
        .appending(path: "fixtures")

    static func load(_ file: String) throws -> JSONValue {
        try JSONValue.parse(String(contentsOf: fixtures.appending(path: file), encoding: .utf8))
    }

    static func caseNames(_ file: String) -> [String] {
        ((try? load(file))?["cases"]?.arrayValue ?? []).compactMap { $0["name"]?.stringValue }
    }

    static func testCase(_ file: String, _ name: String) throws -> JSONValue {
        try #require(try load(file)["cases"]?.arrayValue?.first { $0["name"]?.stringValue == name })
    }

    @Test func fixturesAreFound() {
        #expect(!Self.caseNames("dog-states.json").isEmpty)
        #expect(!Self.caseNames("folds.json").isEmpty)
    }

    @Test(arguments: caseNames("dog-states.json"))
    func poseAndSessionStatusMatchTheFixture(name: String) throws {
        let fixture = try Self.load("dog-states.json")
        let now = try #require(FlexibleDate.parse(fixture["now"]?.stringValue ?? ""))
        let testCase = try Self.testCase("dog-states.json", name)
        let stateJSON = try #require(testCase["state"]).serialized()
        let state = try JSONDecoder().decode(VitalsState.self, from: Data(stateJSON.utf8))

        #expect(state.sessionStatus(at: now).rawValue == testCase["session_status"]?.stringValue)
        #expect(DogStates.pose(for: state, at: now).rawValue == testCase["dog"]?.stringValue)
    }

    @Test(arguments: caseNames("folds.json"))
    func hookSequenceFoldsToTheFixture(name: String) throws {
        let testCase = try Self.testCase("folds.json", name)
        let now = Date(timeIntervalSince1970: 1_793_448_000)
        var state = VitalsState(lastUpdatedUtc: now)

        for step in testCase["steps"]?.arrayValue ?? [] {
            let verb = step["verb"]?.stringValue ?? ""
            // A string payload is sent as-is, which is how the fixtures express malformed input.
            let payload = step["payload"]?.stringValue ?? step["payload"]?.serialized() ?? ""

            if verb.lowercased() == "statusline" {
                if let status = VitalsUpdates.parse(StatusLineEvent.self, from: payload) {
                    state = VitalsUpdates.applyStatusLine(state, status, now: now)
                }
            } else if let activity = VitalsUpdates.activity(forVerb: verb) {
                state = VitalsUpdates.applyActivity(state, activity, hook: VitalsUpdates.parse(HookEvent.self, from: payload), now: now)
            } else if let ended = VitalsUpdates.sessionLifetime(forVerb: verb) {
                state = VitalsUpdates.applySessionLifetime(state, ended: ended, hook: VitalsUpdates.parse(HookEvent.self, from: payload), now: now)
            }
        }

        let actual: [String: JSONValue] = [
            "session_id": .string(state.sessionId),
            "session_name": .string(state.sessionName),
            "model_display_name": .string(state.modelDisplayName),
            "session_cost_usd": state.sessionCostUsd.map { .number($0) } ?? .null,
            "session_duration_ms": state.sessionDurationMs.map { .number(Double($0)) } ?? .null,
            "context_used_percentage": state.contextUsedPercentage.map { .number($0) } ?? .null,
            "session_used_percentage": state.session?.usedPercentage.map { .number($0) } ?? .null,
            "week_used_percentage": state.week?.usedPercentage.map { .number($0) } ?? .null,
            "activity": .string(state.activity.rawValue),
            "waiting_message": .string(state.waitingMessage),
            "session_ended": .bool(state.sessionEndedUtc != nil),
        ]

        for (field, expected) in testCase["expected"]?.objectValue ?? [] {
            let value = try #require(actual[field], "The fixture checks '\(field)', which this test does not map.")
            #expect(Self.normalised(value) == Self.normalised(expected), "\(field)")
        }
    }

    /// Reads the state file the Windows build writes — seven-digit fractions, `+00:00` offsets
    /// and the computed members — so the two builds can read each other's files.
    @Test func readsAStateFileWrittenByTheWindowsBuild() throws {
        let data = try Data(contentsOf: Self.fixtures.appending(path: "state-windows.json"))
        let state = try JSONDecoder().decode(VitalsState.self, from: data)

        #expect(state.activity == .waiting)
        #expect(state.waitingMessage == "Claude needs your permission to use Bash")
        #expect(state.session?.usedPercentage == 31)
        #expect(state.week?.usedPercentage == 12.5)
        #expect(state.sessionDurationMs == 61000)
        #expect(state.modelDisplayName == "Opus 5.5")

        let changed = try #require(state.activityChangedUtc)
        let expected = try #require(FlexibleDate.parse("2026-10-31T11:58:00Z")).addingTimeInterval(0.1234567)
        #expect(abs(changed.timeIntervalSince(expected)) < 0.000_01)
        #expect(state.session?.resetsAt == FlexibleDate.parse("2026-10-31T15:00:00Z"))
    }

    /// Numbers compare by value, not spelling: the fixture says 31 where a Double prints 31.0.
    private static func normalised(_ value: JSONValue) -> JSONValue {
        if case let .number(literal) = value, let double = Double(literal) {
            return .number(double)
        }
        return value
    }
}
