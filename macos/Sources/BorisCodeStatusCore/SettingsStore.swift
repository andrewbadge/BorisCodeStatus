import Foundation

/// Reads and writes `settings.json`. Only the app touches it — the hook process never reads a
/// preference — so a lock is enough to serialise the app's own threads, and the file is replaced
/// atomically so a reader can never see half of one.
///
/// Read on every call rather than cached, so a change applies at the moment of use and a hand edit
/// is picked up without a restart. The file is a few hundred bytes.
///
/// Like every preference, it never throws: an unreadable or unparseable file yields the defaults,
/// and a failed write is reported through the return value, because by then the change has already
/// taken effect in the running process and losing it is the lesser problem.
///
/// There is no `LegacySettings` here: the per-preference marker files it imports predate 1.7 and
/// only ever existed on Windows.
public enum SettingsStore {
    private static let gate = NSLock()

    /// The current settings. `url` is for tests; the default is `VitalsPaths.appSettingsFile`.
    public static func load(url: URL = VitalsPaths.appSettingsFile) -> AppSettings {
        gate.withLock { read(url).settings }
    }

    /// Applies `change` and saves. Returns whether it was persisted.
    @discardableResult
    public static func tryUpdate(url: URL = VitalsPaths.appSettingsFile, _ change: (inout AppSettings) -> Void) -> Bool {
        gate.withLock {
            var (settings, corrupt) = read(url)
            change(&settings)

            if corrupt {
                // About to replace something the user may have been editing by hand: keep it.
                let backup = URL(filePath: url.path + ".bak")
                try? FileManager.default.removeItem(at: backup)
                try? FileManager.default.copyItem(at: url, to: backup)
            }

            return write(settings, to: url)
        }
    }

    private static func read(_ url: URL) -> (settings: AppSettings, corrupt: Bool) {
        guard let data = try? Data(contentsOf: url) else {
            return (AppSettings(), false)
        }

        // Only a file that is not a JSON object at all counts as corrupt: a bad member is
        // forgiven by AppSettings' decoder and costs only itself.
        guard let settings = try? JSONDecoder().decode(AppSettings.self, from: data) else {
            return (AppSettings(), true)
        }

        return (settings, false)
    }

    private static func write(_ settings: AppSettings, to url: URL) -> Bool {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]

        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            // .atomic writes a sibling temporary file and renames it over the original.
            try encoder.encode(settings).write(to: url, options: .atomic)
            return true
        } catch {
            return false
        }
    }
}
