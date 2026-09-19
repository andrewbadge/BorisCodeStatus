namespace FidoRelay.Core.State;

/// <summary>
/// Remembers that the user paused the HTTP service, so it stays paused across a restart —
/// including the automatic one at login.
///
/// A marker file rather than a field in <c>state.json</c>: that file is the wire payload, rewritten
/// constantly by the hook process, and a preference has no business being carried in it or being
/// exposed on <c>/status</c>. Presence of the file is the whole flag; there is nothing to parse
/// and so nothing that can be corrupted into a confusing half-state.
/// </summary>
public static class PausePreference
{
    /// <summary>
    /// Whether the service should start paused. Reads the filesystem on each call — this is asked
    /// once at startup and once per toggle, so caching it would only risk going stale.
    /// </summary>
    public static bool IsPaused(string? path = null)
    {
        try
        {
            return File.Exists(path ?? VitalsPaths.PausedFlagFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable preference: start serving. Coming up running is the recoverable failure —
            // a relay that silently refuses to start looks broken.
            return false;
        }
    }

    /// <summary>
    /// Records the preference. Never throws: failing to persist a pause is worth far less than
    /// the pause itself, which has already taken effect in the running process by this point.
    /// Returns whether it was actually written, so the caller can say so if it matters.
    /// </summary>
    public static bool TrySet(bool paused, string? path = null)
    {
        var file = path ?? VitalsPaths.PausedFlagFile;

        try
        {
            if (paused)
            {
                VitalsPaths.EnsureDataDirectory();
                File.WriteAllText(file, $"Paused at {DateTimeOffset.Now:u} from the FidoRelay tray menu.");
            }
            else
            {
                File.Delete(file);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
