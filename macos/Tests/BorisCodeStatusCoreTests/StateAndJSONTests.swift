import Foundation
import Testing
@testable import BorisCodeStatusCore

struct VitalsStateStoreTests {
    let url = FileManager.default.temporaryDirectory
        .appending(path: "BorisCodeStatusTests-\(UUID().uuidString)")
        .appending(path: "state.json")

    @Test func aWriteIsSeenByAnotherStore() {
        let now = Date(timeIntervalSince1970: 1_793_448_000)
        let writer = VitalsStateStore(url: url)
        let reader = VitalsStateStore(url: url)

        writer.update(now: now) { VitalsUpdates.applyActivity($0, .waiting, hook: HookEvent(message: "Allow Bash?"), now: now) }

        #expect(reader.reload())
        #expect(reader.state.activity == .waiting)
        #expect(reader.state.waitingMessage == "Allow Bash?")
        #expect(reader.state.lastUpdatedUtc == now)
    }

    @Test func eachUpdateStartsFromTheNewestFile() {
        let now = Date(timeIntervalSince1970: 1_793_448_000)
        let first = VitalsStateStore(url: url)
        let second = VitalsStateStore(url: url)

        first.update(now: now) { VitalsUpdates.applySessionLifetime($0, ended: false, hook: HookEvent(sessionId: "s1"), now: now) }
        second.update(now: now) { VitalsUpdates.applyActivity($0, .working, now: now) }

        // The second store never read before writing, but its update re-reads under the lock.
        #expect(first.state.sessionId == "s1")
        #expect(first.state.activity == .working)
    }

    @Test func keepsThePreviousStateWhenTheFileIsCorrupt() throws {
        let now = Date(timeIntervalSince1970: 1_793_448_000)
        let store = VitalsStateStore(url: url)
        store.update(now: now) { VitalsUpdates.applyActivity($0, .idle, now: now) }

        try Data("{ half a file".utf8).write(to: url)

        #expect(!store.reload())
        #expect(store.state.activity == .idle)
    }

    @Test func writesTheWindowsMemberOrder() throws {
        let now = Date(timeIntervalSince1970: 1_793_448_000)
        VitalsStateStore(url: url).update(now: now) { $0 }

        let root = try JSONValue.parse(String(contentsOf: url, encoding: .utf8))
        let windows = try JSONValue.parse(String(contentsOf: SharedFixtureTests.fixtures.appending(path: "state-windows.json"), encoding: .utf8))

        #expect(root.objectValue?.map(\.0) == windows.objectValue?.map(\.0))
    }

    @Test func roundTripsThroughTheFile() throws {
        let now = Date(timeIntervalSince1970: 1_793_448_000.5)
        var state = VitalsState(lastUpdatedUtc: now)
        state.session = RateLimitWindow(usedPercentage: 31.5, resetsAt: now.addingTimeInterval(3600))
        state.sessionDurationMs = 61000
        state.activity = .waiting
        state.activityChangedUtc = now
        state.waitingMessage = "Line one\n\"quoted\" — ünïcode"

        let data = Data(state.json(at: now).serialized().utf8)
        let decoded = try JSONDecoder().decode(VitalsState.self, from: data)

        #expect(decoded == state)
    }
}

struct TimeRuleTests {
    let now = Date(timeIntervalSince1970: 1_793_448_000)

    /// .NET's Math.Round rounds half to even; both builds must agree on the minute.
    @Test func resetsInMinutesRoundsHalfToEven() {
        #expect(RateLimitWindow(resetsAt: now.addingTimeInterval(150)).resetsInMinutes(at: now) == 2)
        #expect(RateLimitWindow(resetsAt: now.addingTimeInterval(90)).resetsInMinutes(at: now) == 2)
        #expect(RateLimitWindow(resetsAt: now.addingTimeInterval(-600)).resetsInMinutes(at: now) == 0)
        #expect(RateLimitWindow().resetsInMinutes(at: now) == nil)
    }

    @Test func activityChangedOnlyMovesOnAChange() {
        let later = now.addingTimeInterval(60)
        let working = VitalsUpdates.applyActivity(VitalsState(), .working, now: now)
        let stillWorking = VitalsUpdates.applyActivity(working, .working, now: later)
        let idle = VitalsUpdates.applyActivity(stillWorking, .idle, now: later)

        #expect(stillWorking.activityChangedUtc == now)
        #expect(idle.activityChangedUtc == later)
    }

    @Test(arguments: ["statusline", "bark", "", "  "])
    func nonLifecycleVerbsMapToNothing(verb: String) {
        #expect(VitalsUpdates.activity(forVerb: verb) == nil)
        #expect(VitalsUpdates.sessionLifetime(forVerb: verb) == nil)
    }

    @Test func epochTimestampsInSecondsAndMilliseconds() {
        #expect(FlexibleDate.parse("1793448000") == now)
        #expect(FlexibleDate.parse("1793448000000") == now)
        #expect(FlexibleDate.parse("not a date") == nil)
    }
}

struct JSONValueTests {
    @Test func roundTripsOrderAndNumberSpelling() throws {
        let text = """
        {
          "z": 1.0,
          "a": [
            true,
            null,
            -2.5e3
          ],
          "m": {},
          "s": "tab\\tquote\\"slash\\\\ \\u00e9 \\ud83d\\udc36"
        }
        """

        let value = try JSONValue.parse(text)

        #expect(value.objectValue?.map(\.0) == ["z", "a", "m", "s"])
        #expect(value["s"]?.stringValue == "tab\tquote\"slash\\ é 🐶")
        #expect(try JSONValue.parse(value.serialized()) == value)
        #expect(value["z"] == .number("1.0"))
    }

    @Test(arguments: ["", "{", "{\"a\":}", "[1,]", "{\"a\":1} x", "tru", "01x", "\"\\q\""])
    func rejectsMalformedInput(text: String) {
        #expect(throws: JSONValue.ParseError.self) { try JSONValue.parse(text) }
    }

    @Test func setReplacesInPlaceAndAppendsNew() {
        var value = JSONValue.object([("a", .number("1")), ("b", .number("2"))])

        value.set("a", .string("x"))
        value.set("c", .null)
        value.remove("b")

        #expect(value == .object([("a", .string("x")), ("c", .null)]))
    }
}
