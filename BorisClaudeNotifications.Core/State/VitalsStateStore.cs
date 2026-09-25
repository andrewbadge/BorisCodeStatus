using System.Text.Json;
using BorisClaudeNotifications.Core.Models;

namespace BorisClaudeNotifications.Core.State;

/// <summary>
/// Single source of truth for <see cref="VitalsState"/>, shared between the short-lived hook
/// process and the long-lived tray/API process.
///
/// There is no IPC: the hook process writes <c>state.json</c> and exits, and the tray process
/// picks the change up via <see cref="StartWatching"/>. Writes are serialised across processes
/// by a named mutex and are atomic (temp file + replace), so a reader never sees a torn file.
/// </summary>
public sealed class VitalsStateStore : IDisposable
{
    private const string MutexName = @"Local\BorisClaudeNotifications.state.lock";
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(2);

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly Lock _gate = new();
    private VitalsState _current = new();
    private DateTime _loadedWriteTimeUtc = DateTime.MinValue;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private bool _disposed;

    /// <summary>Shared instance. Both processes use this; the file underneath is what joins them.</summary>
    public static VitalsStateStore Default { get; } = CreateDefault();

    private static VitalsStateStore CreateDefault()
    {
        VitalsPaths.EnsureDataDirectory();
        return new VitalsStateStore(VitalsPaths.StateFile);
    }

    public VitalsStateStore(string path)
    {
        _path = path;
        TryLoadFromDisk();
    }

    /// <summary>Raised (on a thread-pool thread) when another process changed the state file.</summary>
    public event Action<VitalsState>? Changed;

    /// <summary>
    /// The current state, refreshed from disk if another process has written since the last read.
    /// Cheap enough to call per HTTP request: it stats the file and only re-parses on a change.
    /// </summary>
    public VitalsState Current
    {
        get
        {
            TryLoadFromDisk();
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>
    /// Applies <paramref name="mutate"/> to the newest state on disk and persists the result.
    /// <see cref="VitalsState.LastUpdatedUtc"/> is stamped automatically. Never throws on I/O: a
    /// failed write leaves the previous file intact, which is the right outcome for a status display.
    /// </summary>
    public VitalsState Update(Func<VitalsState, VitalsState> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        var mutex = AcquireMutex(out var held);
        try
        {
            // Re-read under the lock: another process may have written since we last looked.
            TryLoadFromDisk(force: true);

            VitalsState updated;
            lock (_gate)
            {
                updated = mutate(_current) with { LastUpdatedUtc = DateTimeOffset.UtcNow };
                _current = updated;
            }

            TryWriteToDisk(updated);
            return updated;
        }
        finally
        {
            if (held && mutex is not null)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // Not owned by this thread; nothing to release.
                }
            }

            mutex?.Dispose();
        }
    }

    /// <summary>Starts watching the state file so <see cref="Changed"/> fires on cross-process writes.</summary>
    public void StartWatching()
    {
        if (_watcher is not null)
        {
            return;
        }

        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);

        _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnFileTouched;
        _watcher.Created += OnFileTouched;
        _watcher.Renamed += OnFileTouched;
    }

    // A single logical write produces several events (temp file, replace, timestamp update),
    // so coalesce them into one Changed notification.
    private void OnFileTouched(object sender, FileSystemEventArgs e)
    {
        _debounce ??= new Timer(
            _ =>
            {
                if (TryLoadFromDisk(force: true))
                {
                    Changed?.Invoke(Current);
                }
            },
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        _debounce.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
    }

    /// <summary>Returns true if in-memory state was replaced with newer content from disk.</summary>
    private bool TryLoadFromDisk(bool force = false)
    {
        try
        {
            if (!File.Exists(_path))
            {
                return false;
            }

            var writeTime = File.GetLastWriteTimeUtc(_path);
            if (!force && writeTime == _loadedWriteTimeUtc)
            {
                return false;
            }

            var json = ReadAllTextShared(_path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            var loaded = JsonSerializer.Deserialize<VitalsState>(json, JsonOptions);
            if (loaded is null)
            {
                return false;
            }

            lock (_gate)
            {
                _current = loaded;
                _loadedWriteTimeUtc = writeTime;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A half-written or locked file is transient; keep whatever we already had.
            return false;
        }
    }

    // The other process may hold the file open momentarily; share every mode rather than fail.
    private static string ReadAllTextShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void TryWriteToDisk(VitalsState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(state, JsonOptions);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);

            // Replace rather than overwrite so readers see either the old or the new file, never a partial one.
            File.Move(temp, _path, overwrite: true);
            _loadedWriteTimeUtc = File.GetLastWriteTimeUtc(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing one status update is preferable to failing the hook and blocking Claude Code.
        }
    }

    private static Mutex? AcquireMutex(out bool held)
    {
        held = false;
        try
        {
            var mutex = new Mutex(initiallyOwned: false, MutexName);
            try
            {
                held = mutex.WaitOne(MutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                // A process died mid-write; we now own it, and the atomic replace makes recovery safe.
                held = true;
            }

            return mutex;
        }
        catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
        {
            // Proceed unsynchronised rather than drop the update; the write itself is still atomic.
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileTouched;
            _watcher.Created -= OnFileTouched;
            _watcher.Renamed -= OnFileTouched;
            _watcher.Dispose();
            _watcher = null;
        }

        _debounce?.Dispose();
        _debounce = null;
    }
}
