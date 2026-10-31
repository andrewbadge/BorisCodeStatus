namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the tray may ask GitHub whether a newer release exists, every few hours.
///
/// Off by default: it is a network call, and the app makes none unless asked to. A missing or
/// unreadable settings file therefore keeps it quiet. Also remembers the newest release already
/// announced, so the card appears once per release rather than at every login.
/// </summary>
public static class UpdateCheckPreference
{
    public static bool IsEnabled(string? path = null) => SettingsStore.Load(path).UpdateCheckEnabled;

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetEnabled(bool enabled, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { UpdateCheckEnabled = enabled }, path);

    /// <summary>The release last announced, as "major.minor.patch", or null if none has been.</summary>
    public static string? NotifiedVersion(string? path = null) => SettingsStore.Load(path).UpdateNotifiedVersion;

    public static bool TrySetNotifiedVersion(string version, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { UpdateNotifiedVersion = version }, path);
}
