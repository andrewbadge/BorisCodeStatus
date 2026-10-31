import BorisCodeStatusCore
import Foundation

// The single executable Claude Code invokes for both statusLine and lifecycle hooks. Which one it
// is comes from the verb argument each registration passes (`BorisCodeStatusHooks statusline`,
// `... notification`, and so on).
//
// Design constraints, both from Claude Code's side:
//  * It must be fast. Claude Code cancels an in-flight statusLine script when the next event
//    arrives, and a slow script stalls status line updates. So: file I/O only, never network.
//  * It must never fail loudly. A non-zero exit or a hang is worse than a missed update, so every
//    path below exits 0 — including an unknown verb and malformed input. Swift has no catch-all
//    for a trap, so the code here also avoids anything that can trap: no force unwraps, no
//    unchecked indexing.

/// Hard ceiling on the whole run; if anything blocks, abandon the update and leave.
let watchdog: TimeInterval = 3

DispatchQueue.global().asyncAfter(deadline: .now() + watchdog) {
    exit(0)
}

let arguments = CommandLine.arguments.dropFirst()
let verb = arguments.first ?? ""

/// Reads the whole payload from stdin. Returns empty when stdin is a terminal rather than a
/// pipe, so running the helper by hand does not hang waiting for input that will never come.
func readStandardInput() -> String {
    guard isatty(STDIN_FILENO) == 0 else { return "" }
    let data = FileHandle.standardInput.readDataToEndOfFile()
    return String(decoding: data, as: UTF8.self)
}

/// The text Claude Code renders as the status line. We occupy the user's statusLine slot, so
/// returning something useful is better manners than returning nothing.
func render(_ state: VitalsState) -> String {
    func percent(_ value: Double) -> String { String(Int(value.rounded())) + "%" }

    var parts: [String] = []
    if let model = state.modelDisplayName, !model.trimmingCharacters(in: .whitespaces).isEmpty {
        parts.append(model)
    }
    if let session = state.session?.usedPercentage {
        parts.append("5h " + percent(session))
    }
    if let week = state.week?.usedPercentage {
        parts.append("7d " + percent(week))
    }
    if let context = state.contextUsedPercentage {
        parts.append("ctx " + percent(context))
    }
    return parts.joined(separator: "  ")
}

let payload = readStandardInput()
let store = VitalsStateStore.makeDefault()

// One instant for the whole run, so last_updated_utc and last_event_utc agree.
let now = Date()

if verb.lowercased() == "statusline" {
    if let status = VitalsUpdates.parse(StatusLineEvent.self, from: payload) {
        let state = store.update(now: now) { VitalsUpdates.applyStatusLine($0, status, now: now) }
        FileHandle.standardOutput.write(Data(render(state).utf8))
    }
} else if let activity = VitalsUpdates.activity(forVerb: verb) {
    let hook = VitalsUpdates.parse(HookEvent.self, from: payload)
    store.update(now: now) { VitalsUpdates.applyActivity($0, activity, hook: hook, now: now) }
} else if let ended = VitalsUpdates.sessionLifetime(forVerb: verb) {
    let hook = VitalsUpdates.parse(HookEvent.self, from: payload)
    store.update(now: now) { VitalsUpdates.applySessionLifetime($0, ended: ended, hook: hook, now: now) }
} else {
    // Unknown verb: say so on stderr (which Claude Code surfaces in debug output only) and still
    // succeed, so a stale registration never blocks a session.
    FileHandle.standardError.write(Data("BorisCodeStatus: unrecognised verb '\(verb)'.\n".utf8))
}

exit(0)
