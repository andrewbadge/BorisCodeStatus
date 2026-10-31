import Foundation

/// Appends unexpected errors to `error.log` in the data directory, so a failure that either
/// process recovers from — or one it does not — leaves something behind to diagnose.
///
/// Called from the top level only: the hook's catch-all and the app's last-resort handlers.
/// Never throws, and file I/O only, so it is safe in the hook process. Capped at about 1 MB:
/// past that the file is rolled to `error.log.old`, replacing the previous one, so a fault in a
/// loop cannot fill the disk.
public enum ErrorLog {
    private static let maxBytes = 1024 * 1024

    public static func write(_ location: String, _ error: Error?, url: URL = VitalsPaths.errorLogFile) {
        let stamp = Date().formatted(.iso8601.year().month().day().dateSeparator(.dash)
            .time(includingFractionalSeconds: false).timeSeparator(.colon).timeZone(separator: .colon))
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "unknown"
        let detail = error.map { String(reflecting: $0) } ?? "(no error object)"
        let entry = "\(stamp) [\(location)] v\(version) pid \(ProcessInfo.processInfo.processIdentifier)\n\(detail)\n\n"

        let manager = FileManager.default
        try? manager.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)

        if let size = (try? manager.attributesOfItem(atPath: url.path))?[.size] as? Int, size > maxBytes {
            let old = URL(filePath: url.path + ".old")
            try? manager.removeItem(at: old)
            try? manager.moveItem(at: url, to: old)
        }

        // Append without a cross-process lock: an interleaved line between the hook and the app
        // is an acceptable cost. Losing a log line is better than a second failure.
        if let handle = try? FileHandle(forWritingTo: url) {
            defer { try? handle.close() }
            _ = try? handle.seekToEnd()
            try? handle.write(contentsOf: Data(entry.utf8))
        } else {
            try? Data(entry.utf8).write(to: url)
        }
    }
}
