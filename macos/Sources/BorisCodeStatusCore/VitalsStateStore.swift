import Foundation

/// Single source of truth for `VitalsState`, shared between the short-lived hook process and
/// the long-lived menu-bar app.
///
/// There is no IPC: the hook process writes `state.json` and exits, and the app picks the change
/// up by watching the directory. Writes are serialised across processes by `flock` on a sibling
/// `.lock` file and are atomic (temp file + rename), so a reader never sees a torn file.
public final class VitalsStateStore: @unchecked Sendable {
    /// How long to wait for another process's write before going ahead unsynchronised.
    private static let lockTimeout: TimeInterval = 2

    public let url: URL
    private let gate = NSLock()
    private var current = VitalsState(lastUpdatedUtc: Date(timeIntervalSince1970: 0))
    private var loadedModificationDate: Date?

    public init(url: URL) {
        self.url = url
        _ = tryLoadFromDisk(force: true)
    }

    /// The store both processes use; the file underneath is what joins them.
    public static func makeDefault() -> VitalsStateStore {
        VitalsStateStore(url: VitalsPaths.stateFile)
    }

    /// The current state, refreshed from disk if another process has written since the last
    /// read. Cheap to call often: it stats the file and only re-parses on a change.
    public var state: VitalsState {
        _ = tryLoadFromDisk(force: false)
        gate.lock()
        defer { gate.unlock() }
        return current
    }

    /// Re-reads the file and reports whether it held anything new. The app calls this when the
    /// directory watcher fires.
    public func reload() -> Bool {
        tryLoadFromDisk(force: true)
    }

    /// Applies `mutate` to the newest state on disk and persists the result, stamping
    /// `lastUpdatedUtc` with `now`. Never throws on I/O: a failed write leaves the previous file
    /// intact, which is the right outcome for a status display.
    @discardableResult
    public func update(now: Date = Date(), _ mutate: (VitalsState) -> VitalsState) -> VitalsState {
        let lock = acquireLock()
        defer { releaseLock(lock) }

        // Re-read under the lock: another process may have written since we last looked.
        _ = tryLoadFromDisk(force: true)

        gate.lock()
        var updated = mutate(current)
        updated.lastUpdatedUtc = now
        current = updated
        gate.unlock()

        tryWriteToDisk(updated, now: now)
        return updated
    }

    private func tryLoadFromDisk(force: Bool) -> Bool {
        let modified = (try? FileManager.default.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date
        guard let modified else { return false }

        gate.lock()
        let unchanged = !force && modified == loadedModificationDate
        gate.unlock()
        if unchanged {
            return false
        }

        // A half-written or unreadable file is transient; keep whatever we already had.
        guard let data = try? Data(contentsOf: url), !data.isEmpty,
              let loaded = try? JSONDecoder().decode(VitalsState.self, from: data)
        else { return false }

        gate.lock()
        defer { gate.unlock() }
        current = loaded
        loadedModificationDate = modified
        return true
    }

    private func tryWriteToDisk(_ state: VitalsState, now: Date) {
        do {
            try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            // .atomic writes a temporary file and renames it over the target, so readers see
            // either the old or the new file, never a partial one.
            try Data(state.json(at: now).serialized().utf8).write(to: url, options: .atomic)
            let modified = (try? FileManager.default.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date
            gate.lock()
            loadedModificationDate = modified
            gate.unlock()
        } catch {
            // Losing one status update is preferable to failing the hook and blocking Claude Code.
        }
    }

    /// Returns a descriptor holding an exclusive `flock`, or nil to proceed unsynchronised rather
    /// than drop the update — the write itself is still atomic. A lock held by a process that
    /// died is released by the kernel, so there is no abandoned-lock case to handle.
    private func acquireLock() -> Int32? {
        let lockPath = url.path + ".lock"
        try? FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let fd = open(lockPath, O_CREAT | O_RDWR | O_CLOEXEC, 0o644)
        guard fd >= 0 else { return nil }

        let deadline = Date().addingTimeInterval(VitalsStateStore.lockTimeout)
        while flock(fd, LOCK_EX | LOCK_NB) != 0 {
            guard errno == EWOULDBLOCK || errno == EINTR, Date() < deadline else {
                close(fd)
                return nil
            }
            usleep(10_000)
        }
        return fd
    }

    private func releaseLock(_ fd: Int32?) {
        guard let fd else { return }
        flock(fd, LOCK_UN)
        close(fd)
    }
}
