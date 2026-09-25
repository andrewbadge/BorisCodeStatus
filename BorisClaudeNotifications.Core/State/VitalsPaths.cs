namespace BorisClaudeNotifications.Core.State;

/// <summary>Well-known on-disk locations shared by the hook process and the tray process.</summary>
public static class VitalsPaths
{
    /// <summary>%LOCALAPPDATA%\BorisClaudeNotifications</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BorisClaudeNotifications");

    public static string StateFile => Path.Combine(DataDirectory, "state.json");

    /// <summary>Records when the fallback usage API was last called, to enforce the interval across processes.</summary>
    public static string UsageApiStampFile => Path.Combine(DataDirectory, "usage-api.stamp");

    /// <summary>Present when the user has paused the HTTP service. Its existence is the flag.</summary>
    public static string PausedFlagFile => Path.Combine(DataDirectory, "http-paused.flag");

    /// <summary>
    /// Present when the user has turned notifications off. Marks the non-default setting, since
    /// notifications are on out of the box.
    /// </summary>
    public static string NotificationsDisabledFlagFile =>
        Path.Combine(DataDirectory, "notifications-disabled.flag");

    /// <summary>~\.claude</summary>
    public static string ClaudeDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude");

    public static string CredentialsFile => Path.Combine(ClaudeDirectory, ".credentials.json");

    public static string SettingsFile => Path.Combine(ClaudeDirectory, "settings.json");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);
}
