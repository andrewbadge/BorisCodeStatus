namespace BorisCodeStatus.Core.State;

/// <summary>Well-known on-disk locations shared by the hook process and the tray process.</summary>
public static class VitalsPaths
{
    /// <summary>%LOCALAPPDATA%\BorisCodeStatus</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BorisCodeStatus");

    public static string StateFile => Path.Combine(DataDirectory, "state.json");

    /// <summary>
    /// Every tray preference. Absent until the user first changes one, which is when the defaults
    /// are written out. See <see cref="AppSettings"/>; versions before 1.7 kept each preference in
    /// a file of its own, which <see cref="SettingsStore"/> imports once.
    /// </summary>
    public static string AppSettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Records when the fallback usage API was last called, to enforce the interval across processes.</summary>
    public static string UsageApiStampFile => Path.Combine(DataDirectory, "usage-api.stamp");

    /// <summary>Unexpected exceptions from either process. See <see cref="ErrorLog"/>.</summary>
    public static string ErrorLogFile => Path.Combine(DataDirectory, "error.log");

    /// <summary>~\.claude</summary>
    public static string ClaudeDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude");

    public static string CredentialsFile => Path.Combine(ClaudeDirectory, ".credentials.json");

    public static string SettingsFile => Path.Combine(ClaudeDirectory, "settings.json");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
