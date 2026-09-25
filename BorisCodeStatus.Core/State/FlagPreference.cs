namespace BorisCodeStatus.Core.State;

/// <summary>
/// A preference stored as the presence or absence of a marker file.
///
/// Used for the handful of settings the tray owns. They stay out of <c>state.json</c> on purpose:
/// that file is the wire payload, rewritten constantly by the hook process, and a preference has
/// no business being carried in it or exposed on <c>/status</c>. A file's existence is the whole
/// value, so there is nothing to parse and nothing that can corrupt into a confusing half-state.
///
/// By convention the file marks the <em>non-default</em> setting, so a missing or unreadable
/// preference always yields the default.
/// </summary>
public static class FlagPreference
{
    /// <summary>
    /// Whether the marker is present. Reads the filesystem on each call — these are read at
    /// startup and on each toggle, so caching would only risk going stale.
    /// </summary>
    public static bool IsSet(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable preference: fall back to the default rather than guessing.
            return false;
        }
    }

    /// <summary>
    /// Writes or removes the marker. Never throws: by the time this is called the change has
    /// already taken effect in the running process, and losing the preference is the lesser
    /// problem. Returns whether it was persisted, so the caller can say so if it matters.
    /// </summary>
    public static bool TrySet(string path, bool set, string reason)
    {
        try
        {
            if (set)
            {
                VitalsPaths.EnsureDataDirectory();

                // Content is for whoever finds the file, not for this code — only its existence
                // is ever read back.
                File.WriteAllText(path, $"{reason} at {DateTimeOffset.Now:u} from the BorisCodeStatus tray menu.");
            }
            else
            {
                File.Delete(path);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
