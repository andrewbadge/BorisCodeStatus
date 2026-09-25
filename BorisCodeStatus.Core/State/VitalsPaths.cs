namespace BorisCodeStatus.Core.State;

/// <summary>Well-known on-disk locations shared by the hook process and the tray process.</summary>
public static class VitalsPaths
{
    /// <summary>%LOCALAPPDATA%\BorisCodeStatus</summary>
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BorisCodeStatus");

    public static string StateFile => Path.Combine(DataDirectory, "state.json");

    /// <summary>Records when the fallback usage API was last called, to enforce the interval across processes.</summary>
    public static string UsageApiStampFile => Path.Combine(DataDirectory, "usage-api.stamp");

    /// <summary>
    /// Present when the user has switched the HTTP service on. Marks the non-default setting, since
    /// the endpoint is closed out of the box. (Earlier versions defaulted to on and wrote
    /// <c>http-paused.flag</c> instead; that file is now ignored.)
    /// </summary>
    public static string HttpEnabledFlagFile => Path.Combine(DataDirectory, "http-enabled.flag");

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
