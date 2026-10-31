import Foundation

/// Well-known on-disk locations shared by the hook process and the menu-bar app.
public enum VitalsPaths {
    /// `~/Library/Application Support/BorisCodeStatus`. The space in "Application Support" never
    /// reaches a shell command: only Swift file APIs open this directory.
    public static let dataDirectory: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appending(path: "Library/Application Support")
        return base.appending(path: "BorisCodeStatus", directoryHint: .isDirectory)
    }()

    public static var stateFile: URL { dataDirectory.appending(path: "state.json") }

    /// Every app preference. Absent until the user first changes one.
    public static var appSettingsFile: URL { dataDirectory.appending(path: "settings.json") }

    /// Unexpected errors from either process. See `ErrorLog`.
    public static var errorLogFile: URL { dataDirectory.appending(path: "error.log") }

    /// `~/.claude`
    public static let claudeDirectory: URL =
        FileManager.default.homeDirectoryForCurrentUser.appending(path: ".claude", directoryHint: .isDirectory)

    /// `~/.claude/settings.json`, where the hooks are registered.
    public static var claudeSettingsFile: URL { claudeDirectory.appending(path: "settings.json") }
}
