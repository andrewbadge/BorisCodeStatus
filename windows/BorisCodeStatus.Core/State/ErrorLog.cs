using System.Globalization;
using System.Reflection;

namespace BorisCodeStatus.Core.State;

/// <summary>
/// Appends unexpected exceptions to <c>%LOCALAPPDATA%\BorisCodeStatus\error.log</c>, so a failure
/// that the app recovers from — or one it does not — leaves something behind to diagnose.
///
/// Called from the top level of both processes only: the tray's unhandled-exception handlers and
/// the hook's catch-all. Expected failures that are already handled where they happen (a locked
/// settings.json, a busy port) are not logged; this is for the ones nobody planned for.
///
/// Never throws, and file I/O only, so it is safe in the hook process. Capped at about 1 MB: past
/// that the file is rolled to <c>error.log.old</c>, replacing the previous one, so a fault in a
/// loop cannot fill the disk.
/// </summary>
public static class ErrorLog
{
    private const long MaxBytes = 1024 * 1024;

    // Two threads in the tray can fail at once; the hook and the tray are separate processes, and
    // an interleaved line between them is an acceptable cost for not taking a cross-process lock.
    private static readonly object Gate = new();

    public static void Write(string where, Exception? exception, string? path = null)
    {
        path ??= VitalsPaths.ErrorLogFile;
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown";
        var entry = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} [{where}] v{version} pid {Environment.ProcessId}{Environment.NewLine}"
            + $"{exception?.ToString() ?? "(no exception object)"}{Environment.NewLine}{Environment.NewLine}");

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var file = new FileInfo(path);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllText(path, entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nowhere left to report it. Losing a log line is better than a second failure.
        }
    }
}
