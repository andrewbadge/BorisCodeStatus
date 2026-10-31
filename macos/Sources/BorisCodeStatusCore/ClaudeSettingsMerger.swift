import Foundation

/// Outcome of a settings merge, so the caller can surface conflicts instead of hiding them.
public struct SettingsMergeResult: Equatable, Sendable {
    public var changed: Bool
    public var warnings: [String]

    public init(changed: Bool, warnings: [String]) {
        self.changed = changed
        self.warnings = warnings
    }
}

/// Registers this app's statusLine and lifecycle hooks in `~/.claude/settings.json`, and removes
/// them again.
///
/// The file is read-modify-written as an order-preserving JSON tree, never regenerated: it
/// routinely holds unrelated user configuration that must survive untouched. An existing
/// third-party `statusLine` is left alone and reported as a warning rather than clobbered, and a
/// file that cannot be parsed is not touched at all.
public enum ClaudeSettingsMerger {
    /// What identifies one of our commands. Distinct from the Windows build's
    /// `BorisCodeStatus.Hooks.exe`, so neither build mistakes the other's entry for its own.
    public static let hooksExecutableName = "BorisCodeStatusHooks"

    /// Lifecycle hooks to register, as (settings key, verb argument, matcher). The same list as
    /// the Windows build.
    static let lifecycleHooks: [(event: String, verb: String, matcher: String?)] = [
        ("Notification", "notification", nil),
        ("Stop", "stop", nil),
        ("UserPromptSubmit", "userpromptsubmit", nil),
        ("PreToolUse", "pretooluse", "*"),
        ("SessionStart", "sessionstart", nil),
        ("SessionEnd", "sessionend", nil),
    ]

    /// Ensures the hook entries point at `hooksPath`. Idempotent: running it again after an
    /// upgrade or a move rewrites stale paths but adds nothing.
    ///
    /// Refuses a path that will not outlive this run — see `HookPathGuard` — because the hook
    /// never reports its own failures, so a registration pointing at a vanished file fails
    /// silently forever.
    public static func merge(hooksPath: String, settingsURL: URL = VitalsPaths.claudeSettingsFile) -> SettingsMergeResult {
        if let problem = HookPathGuard.problem(with: hooksPath) {
            return SettingsMergeResult(changed: false, warnings: [problem])
        }

        return edit(settingsURL, createIfMissing: true) { root, warnings in
            mergeStatusLine(&root, hooksPath, &warnings)
            mergeLifecycleHooks(&root, hooksPath)
        }
    }

    /// Removes our statusLine and hook entries, leaving everything else as it was. A Mac app is
    /// removed by dragging it to the Bin, with no uninstaller to clean up, so this is what stops
    /// the entries outliving the app.
    public static func unregister(settingsURL: URL = VitalsPaths.claudeSettingsFile) -> SettingsMergeResult {
        edit(settingsURL, createIfMissing: false) { root, _ in
            if let command = root["statusLine"]?["command"]?.stringValue, isOurs(command) {
                root.remove("statusLine")
            }
            removeOurLifecycleHooks(&root)
        }
    }

    /// The hooks path our statusLine entry currently points at, or nil if it is not ours. Lets the
    /// app warn when the registered path no longer exists.
    public static func registeredHooksPath(settingsURL: URL = VitalsPaths.claudeSettingsFile) -> String? {
        guard let text = try? String(contentsOf: settingsURL, encoding: .utf8),
              let root = try? JSONValue.parse(text),
              let command = root["statusLine"]?["command"]?.stringValue,
              isOurs(command)
        else { return nil }
        return unquotedPath(command)
    }

    // MARK: - Editing

    private static func edit(
        _ url: URL,
        createIfMissing: Bool,
        _ change: (inout JSONValue, inout [String]) -> Void
    ) -> SettingsMergeResult {
        var warnings: [String] = []
        let existed = FileManager.default.fileExists(atPath: url.path)
        guard existed || createIfMissing else { return SettingsMergeResult(changed: false, warnings: []) }

        var root = JSONValue.object([])
        if existed {
            guard let text = try? String(contentsOf: url, encoding: .utf8) else {
                return SettingsMergeResult(changed: false, warnings: ["Could not read \(url.path); left unchanged."])
            }
            if !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                do {
                    root = try JSONValue.parse(text)
                } catch {
                    // Refuse to touch a file we cannot parse; overwriting it would destroy user config.
                    return SettingsMergeResult(changed: false, warnings: ["Could not parse \(url.path) (\(error)); left unchanged."])
                }
            }
            guard root.objectValue != nil else {
                return SettingsMergeResult(changed: false, warnings: ["\(url.path) is not a JSON object; left unchanged."])
            }
        }

        let before = root
        change(&root, &warnings)
        if existed && root == before {
            return SettingsMergeResult(changed: false, warnings: warnings)
        }

        do {
            try writeAtomically(root, to: url, backup: existed)
        } catch {
            warnings.append("Could not write \(url.path) (\(error.localizedDescription)).")
            return SettingsMergeResult(changed: false, warnings: warnings)
        }
        return SettingsMergeResult(changed: true, warnings: warnings)
    }

    private static func mergeStatusLine(_ root: inout JSONValue, _ hooksPath: String, _ warnings: inout [String]) {
        if let existing = root["statusLine"], existing.objectValue != nil,
           !isOurs(existing["command"]?.stringValue) {
            warnings.append(
                "An existing statusLine command was left in place; BorisCodeStatus will not receive " +
                "usage data until it is replaced. Remove the 'statusLine' entry from settings.json to switch.")
            return
        }

        root.set("statusLine", .object([
            ("type", .string("command")),
            ("command", .string(command(hooksPath, "statusline"))),
        ]))
    }

    private static func mergeLifecycleHooks(_ root: inout JSONValue, _ hooksPath: String) {
        var hooks = root["hooks"].flatMap { $0.objectValue != nil ? $0 : nil } ?? .object([])

        for (event, verb, matcher) in lifecycleHooks {
            var matchers = hooks[event]?.arrayValue ?? []
            let desired = command(hooksPath, verb)

            if !rewriteExistingEntry(&matchers, desired) {
                var entry: [(String, JSONValue)] = []
                if let matcher {
                    entry.append(("matcher", .string(matcher)))
                }
                entry.append(("hooks", .array([.object([
                    ("type", .string("command")),
                    ("command", .string(desired)),
                ])])))
                matchers.append(.object(entry))
            }

            hooks.set(event, .array(matchers))
        }

        root.set("hooks", hooks)
    }

    /// Rewrites our own entry in place if present, which is what repairs a moved or upgraded app.
    private static func rewriteExistingEntry(_ matchers: inout [JSONValue], _ desired: String) -> Bool {
        for index in matchers.indices {
            guard var inner = matchers[index]["hooks"]?.arrayValue else { continue }
            for hookIndex in inner.indices where isOurs(inner[hookIndex]["command"]?.stringValue) {
                inner[hookIndex].set("command", .string(desired))
                matchers[index].set("hooks", .array(inner))
                return true
            }
        }
        return false
    }

    /// Drops our commands, then any matcher entry and any event list left empty by that — but
    /// only those we emptied, so a user's own empty entries stay as they were.
    private static func removeOurLifecycleHooks(_ root: inout JSONValue) {
        guard var hooks = root["hooks"], let events = hooks.objectValue else { return }

        for (event, value) in events {
            guard let matchers = value.arrayValue else { continue }
            var kept: [JSONValue] = []
            var removedAny = false

            for matcher in matchers {
                guard let inner = matcher["hooks"]?.arrayValue else {
                    kept.append(matcher)
                    continue
                }
                let remaining = inner.filter { !isOurs($0["command"]?.stringValue) }
                if remaining.count == inner.count {
                    kept.append(matcher)
                    continue
                }
                removedAny = true
                if !remaining.isEmpty {
                    var trimmed = matcher
                    trimmed.set("hooks", .array(remaining))
                    kept.append(trimmed)
                }
            }

            if removedAny {
                if kept.isEmpty {
                    hooks.remove(event)
                } else {
                    hooks.set(event, .array(kept))
                }
            }
        }

        root.set("hooks", hooks)
    }

    // MARK: - Commands

    static func isOurs(_ command: String?) -> Bool {
        command?.contains(hooksExecutableName) == true
    }

    /// Single-quotes the path for the POSIX shell Claude Code runs the command through. The
    /// bundle and helper names have no spaces, but a user can install the app anywhere.
    static func command(_ hooksPath: String, _ verb: String) -> String {
        "'" + hooksPath.replacingOccurrences(of: "'", with: "'\\''") + "' " + verb
    }

    private static func unquotedPath(_ command: String) -> String {
        guard command.hasPrefix("'"), let verbStart = command.range(of: "' ", options: .backwards) else {
            return command.split(separator: " ").first.map(String.init) ?? command
        }
        return String(command[command.index(after: command.startIndex)..<verbStart.lowerBound])
            .replacingOccurrences(of: "'\\''", with: "'")
    }

    private static func writeAtomically(_ root: JSONValue, to url: URL, backup: Bool) throws {
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)

        if backup {
            // One-time safety net; never overwritten, so the pre-install file is always recoverable.
            let backupURL = URL(filePath: url.path + ".boriscodestatus.bak")
            if !FileManager.default.fileExists(atPath: backupURL.path) {
                try FileManager.default.copyItem(at: url, to: backupURL)
            }
        }

        try Data((root.serialized() + "\n").utf8).write(to: url, options: .atomic)
    }
}

/// Decides whether a hooks path is safe to write into `~/.claude/settings.json`.
///
/// This is the Mac form of the Windows build's worst trap. If the registered path stops
/// existing, every hook fails silently forever, because the hook is built never to report errors.
public enum HookPathGuard {
    /// Why `path` must not be registered, or nil if it is fine.
    public static func problem(with path: String) -> String? {
        if path.contains("/AppTranslocation/") {
            // macOS runs a quarantined app that has not been moved from a randomised read-only
            // copy, which disappears when the app quits.
            return "BorisCodeStatus is running from a temporary location macOS created for it, so its hooks were not registered. " +
                "Move BorisCodeStatus.app to the Applications folder and open it again."
        }
        if path.contains("/DerivedData/") || path.contains("/.build/") {
            return "BorisCodeStatus is running from a build folder (\(path)), so its hooks were not registered. " +
                "Register them from an installed copy instead."
        }
        if !FileManager.default.isExecutableFile(atPath: path) {
            return "The hook helper at \(path) is missing or not executable, so the hooks were not registered."
        }
        return nil
    }
}
