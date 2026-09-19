namespace FidoRelay.Core.State;

/// <summary>Well-known on-disk locations shared by the hook process and the tray process.</summary>
public static class VitalsPaths
{
    /// <summary>%LOCALAPPDATA%\FidoRelay</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FidoRelay");

    public static string StateFile => Path.Combine(DataDirectory, "state.json");

    /// <summary>Records when the fallback usage API was last called, to enforce the interval across processes.</summary>
    public static string UsageApiStampFile => Path.Combine(DataDirectory, "usage-api.stamp");

    /// <summary>Present when the user has paused the HTTP service. Its existence is the flag.</summary>
    public static string PausedFlagFile => Path.Combine(DataDirectory, "http-paused.flag");

    /// <summary>~\.claude</summary>
    public static string ClaudeDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude");

    public static string CredentialsFile => Path.Combine(ClaudeDirectory, ".credentials.json");

    public static string SettingsFile => Path.Combine(ClaudeDirectory, "settings.json");

    /// <summary>%LOCALAPPDATA%\ClaudeVitals — where state lived before the rename to FidoRelay.</summary>
    private static string LegacyDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClaudeVitals");

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        TryMigrateLegacyData();
    }

    /// <summary>
    /// Moves state left behind by a pre-rename install into the new directory, once.
    /// Without this an upgrade silently starts from zero — the display would blank out until the
    /// next statusLine event, and the usage-API throttle stamp would be lost, freeing the client
    /// to call an endpoint that punishes exactly that.
    ///
    /// Copy rather than move, and never overwrite: a newer file in the new location always wins,
    /// and leaving the old directory in place keeps this safe to run repeatedly. Failures are
    /// swallowed for the same reason every other I/O path here is — a status display that starts
    /// empty is a far better outcome than one that refuses to start.
    /// </summary>
    private static void TryMigrateLegacyData()
    {
        try
        {
            if (!Directory.Exists(LegacyDataDirectory))
            {
                return;
            }

            foreach (var source in Directory.EnumerateFiles(LegacyDataDirectory))
            {
                var destination = Path.Combine(DataDirectory, Path.GetFileName(source));
                if (!File.Exists(destination))
                {
                    File.Copy(source, destination);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the app works from an empty state directory.
        }
    }
}
