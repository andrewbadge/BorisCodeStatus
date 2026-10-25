namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the tray may raise a notification when Claude Code is waiting on the user.
///
/// Enabled by default, so a missing or unreadable settings file gives the waiting card. Like the
/// HTTP preference, it survives a restart.
/// </summary>
public static class NotificationPreference
{
    public static bool AreEnabled(string? path = null) => SettingsStore.Load(path).NotificationsEnabled;

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetEnabled(bool enabled, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { NotificationsEnabled = enabled }, path);

    /// <summary>
    /// The card for the other moment — Claude finishing a turn. Off by default, the opposite of
    /// the waiting card: a turn ends far more often than a prompt appears, so a card for it must
    /// be asked for.
    /// </summary>
    public static bool IdleCardEnabled(string? path = null) => SettingsStore.Load(path).IdleCardEnabled;

    public static bool TrySetIdleCardEnabled(bool enabled, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { IdleCardEnabled = enabled }, path);
}
