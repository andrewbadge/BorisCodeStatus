namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the tray may raise a notification when Claude Code is waiting on the user.
///
/// Enabled by default, so the marker file means <em>disabled</em>: that way a missing or
/// unreadable preference gives the default, and there is no first-run write. Like the HTTP
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

    /// <summary>
    /// The card for the other moment — Claude finishing a turn. Off by default, the opposite of
    /// the waiting card: a turn ends far more often than a prompt appears, so a card for it must
    /// be asked for. The marker therefore means enabled.
    /// </summary>
    public static bool IdleCardEnabled(string? path = null) =>
        FlagPreference.IsSet(path ?? VitalsPaths.IdleCardFlagFile);

    public static bool TrySetIdleCardEnabled(bool enabled, string? path = null) =>
        FlagPreference.TrySet(path ?? VitalsPaths.IdleCardFlagFile, set: enabled, reason: "Idle card turned on");
}
