import Foundation
import Testing
@testable import BorisCodeStatusCore

/// Covers registering and removing hooks in `~/.claude/settings.json`, which holds the user's own
/// configuration and must never be regenerated.
struct ClaudeSettingsMergerTests {
    let directory: URL
    let settings: URL
    let hooksPath: String

    init() throws {
        directory = FileManager.default.temporaryDirectory.appending(path: "BorisCodeStatusTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        settings = directory.appending(path: "settings.json")

        // The guard insists on an executable that exists, so give it one.
        let helper = directory.appending(path: "BorisCodeStatus.app/Contents/Helpers/BorisCodeStatusHooks")
        try FileManager.default.createDirectory(at: helper.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data("#!/bin/sh\n".utf8).write(to: helper)
        try FileManager.default.setAttributes([.posixPermissions: 0o755], ofItemAtPath: helper.path)
        hooksPath = helper.path
    }

    func read() throws -> JSONValue {
        try JSONValue.parse(String(contentsOf: settings, encoding: .utf8))
    }

    func write(_ text: String) throws {
        try Data(text.utf8).write(to: settings)
    }

    func commands(_ root: JSONValue, _ event: String) -> [String] {
        (root["hooks"]?[event]?.arrayValue ?? []).flatMap { matcher in
            (matcher["hooks"]?.arrayValue ?? []).compactMap { $0["command"]?.stringValue }
        }
    }

    @Test func createsTheFileWhenMissing() throws {
        let result = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        #expect(result.changed)
        #expect(result.warnings.isEmpty)
        let root = try read()
        #expect(root["statusLine"]?["command"]?.stringValue == "'\(hooksPath)' statusline")
        for (event, verb, _) in ClaudeSettingsMerger.lifecycleHooks {
            #expect(commands(root, event) == ["'\(hooksPath)' \(verb)"])
        }
        #expect(root["hooks"]?["PreToolUse"]?.arrayValue?.first?["matcher"]?.stringValue == "*")
    }

    @Test func keepsUnrelatedConfigurationAndItsOrder() throws {
        try write("""
        {
          "model": "opus",
          "permissions": { "allow": ["Bash(ls:*)"], "deny": [] },
          "cleanupPeriodDays": 30.0,
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ]
          },
          "theme": "dark"
        }
        """)

        #expect(ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings).changed)

        let root = try read()
        let keys = root.objectValue?.map(\.0)
        #expect(Array((keys ?? []).prefix(4)) == ["model", "permissions", "cleanupPeriodDays", "hooks"])
        #expect(root["theme"]?.stringValue == "dark")
        #expect(root["cleanupPeriodDays"] == .number("30.0"))
        #expect(root["permissions"]?["allow"]?.arrayValue == [.string("Bash(ls:*)")])
        #expect(commands(root, "Stop") == ["say done", "'\(hooksPath)' stop"])
    }

    @Test func isIdempotent() throws {
        _ = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)
        let first = try String(contentsOf: settings, encoding: .utf8)

        let again = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        #expect(!again.changed)
        #expect(try String(contentsOf: settings, encoding: .utf8) == first)
    }

    @Test func rewritesAStalePathInPlace() throws {
        try write("""
        {
          "statusLine": { "type": "command", "command": "'/Old/BorisCodeStatus.app/Contents/Helpers/BorisCodeStatusHooks' statusline" },
          "hooks": { "Stop": [ { "hooks": [ { "type": "command", "command": "'/Old/BorisCodeStatus.app/Contents/Helpers/BorisCodeStatusHooks' stop" } ] } ] }
        }
        """)

        #expect(ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings).changed)

        let root = try read()
        #expect(root["statusLine"]?["command"]?.stringValue == "'\(hooksPath)' statusline")
        #expect(commands(root, "Stop") == ["'\(hooksPath)' stop"])
    }

    @Test func leavesAThirdPartyStatusLineAloneWithAWarning() throws {
        try write(#"{ "statusLine": { "type": "command", "command": "~/bin/my-status" } }"#)

        let result = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        #expect(result.warnings.count == 1)
        #expect(try read()["statusLine"]?["command"]?.stringValue == "~/bin/my-status")
        // The lifecycle hooks still go in: they do not compete with anything.
        #expect(commands(try read(), "Notification") == ["'\(hooksPath)' notification"])
    }

    @Test func refusesAFileItCannotParse() throws {
        let broken = #"{ "model": "opus", }"#
        try write(broken)

        let result = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        #expect(!result.changed)
        #expect(result.warnings.first?.contains("Could not parse") == true)
        #expect(try String(contentsOf: settings, encoding: .utf8) == broken)
    }

    @Test func backsTheOriginalUpOnceAndNeverOverwritesIt() throws {
        let original = #"{ "model": "opus" }"#
        try write(original)
        let backup = URL(filePath: settings.path + ".boriscodestatus.bak")

        _ = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)
        _ = ClaudeSettingsMerger.unregister(settingsURL: settings)
        _ = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        #expect(try String(contentsOf: backup, encoding: .utf8) == original)
    }

    @Test func unregisterRemovesOnlyOurEntries() throws {
        try write("""
        {
          "model": "opus",
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ],
            "PostToolUse": []
          }
        }
        """)
        _ = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        let result = ClaudeSettingsMerger.unregister(settingsURL: settings)

        #expect(result.changed)
        let root = try read()
        #expect(root["statusLine"] == nil)
        #expect(root["model"]?.stringValue == "opus")
        #expect(commands(root, "Stop") == ["say done"])
        // Event lists we emptied go; the user's own empty one stays.
        #expect(root["hooks"]?["Notification"] == nil)
        #expect(root["hooks"]?["PostToolUse"] == .array([]))
    }

    @Test func unregisterLeavesAThirdPartyStatusLine() throws {
        try write(#"{ "statusLine": { "type": "command", "command": "~/bin/my-status" } }"#)

        _ = ClaudeSettingsMerger.unregister(settingsURL: settings)

        #expect(try read()["statusLine"]?["command"]?.stringValue == "~/bin/my-status")
    }

    @Test func unregisterDoesNotCreateAMissingFile() {
        #expect(!ClaudeSettingsMerger.unregister(settingsURL: settings).changed)
        #expect(!FileManager.default.fileExists(atPath: settings.path))
    }

    @Test func reportsTheRegisteredPath() {
        _ = ClaudeSettingsMerger.merge(hooksPath: hooksPath, settingsURL: settings)

        #expect(ClaudeSettingsMerger.registeredHooksPath(settingsURL: settings) == hooksPath)
    }

    @Test func quotesAPathWithSpacesAndQuotes() {
        let command = ClaudeSettingsMerger.command("/Users/o'brien/My Apps/BorisCodeStatusHooks", "stop")

        #expect(command == #"'/Users/o'\''brien/My Apps/BorisCodeStatusHooks' stop"#)
    }

    @Test(arguments: [
        "/private/var/folders/xy/T/AppTranslocation/1234/d/BorisCodeStatus.app/Contents/Helpers/BorisCodeStatusHooks",
        "/Users/me/Library/Developer/Xcode/DerivedData/BorisCodeStatus/Build/Products/Debug/BorisCodeStatusHooks",
        "/Users/me/src/BorisCodeStatus/macos/.build/debug/BorisCodeStatusHooks",
        "/Applications/BorisCodeStatus.app/Contents/Helpers/DoesNotExist",
    ])
    func refusesAPathThatWillNotLast(path: String) throws {
        let result = ClaudeSettingsMerger.merge(hooksPath: path, settingsURL: settings)

        #expect(!result.changed)
        #expect(result.warnings.count == 1)
        #expect(!FileManager.default.fileExists(atPath: settings.path))
    }
}
