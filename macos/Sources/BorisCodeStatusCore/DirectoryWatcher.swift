import Foundation

/// Calls back when anything in a directory changes, coalescing bursts into one call.
///
/// Watches the directory rather than `state.json` itself: every write replaces the file by
/// renaming a temporary one over it, which gives it a new inode, so a watch on the file would go
/// dead after the first write. A rename into the directory is a write to the directory, which this
/// sees every time.
public final class DirectoryWatcher: @unchecked Sendable {
    /// One logical write produces several events (temp file created, renamed over the target),
    /// so wait this long after the last one before calling back.
    public static let debounce: TimeInterval = 0.15

    private let source: DispatchSourceFileSystemObject
    private let queue: DispatchQueue
    private var pending: DispatchWorkItem?

    /// Starts watching `directory`, creating it if needed, and calls `onChange` on `queue`.
    /// Returns nil if the directory cannot be opened.
    public init?(directory: URL, queue: DispatchQueue = .main, onChange: @escaping @Sendable () -> Void) {
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let fd = open(directory.path, O_EVTONLY)
        guard fd >= 0 else { return nil }

        self.queue = queue
        source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .delete], queue: queue)
        source.setEventHandler { [weak self] in
            guard let self else { return }
            self.pending?.cancel()
            let work = DispatchWorkItem(block: onChange)
            self.pending = work
            self.queue.asyncAfter(deadline: .now() + DirectoryWatcher.debounce, execute: work)
        }
        source.setCancelHandler { close(fd) }
        source.resume()
    }

    public func cancel() {
        pending?.cancel()
        source.cancel()
    }

    deinit {
        source.cancel()
    }
}
