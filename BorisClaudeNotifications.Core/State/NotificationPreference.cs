namespace BorisClaudeNotifications.Core.State;

/// <summary>
/// Whether the tray may raise a notification when Claude Code is waiting on the user.
///
/// Enabled by default, so the marker file means <em>disabled</em>: that way a missing or
/// unreadable preference gives the default, and there is no first-run write. Like the pause
/// preference, it survives a restart.
/// </summary>
public static class NotificationPreference
{
    public static bool AreEnabled(string? path = null) =>
        !FlagPreference.IsSet(path ?? VitalsPaths.NotificationsDisabledFlagFile);

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetEnabled(bool enabled, string? path = null) =>
        FlagPreference.TrySet(
            path ?? VitalsPaths.NotificationsDisabledFlagFile,
            set: !enabled,
            reason: "Notifications disabled");
}
