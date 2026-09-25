namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the relay may call the undocumented <c>/api/oauth/usage</c> endpoint for the
/// Sonnet-only weekly figure.
///
/// Off by default: it is the only thing in the app that reads the Claude Code OAuth token or
/// talks to the network, for one figure most displays do not show, from an endpoint that is not a
/// contract. So the marker file means <em>enabled</em>, and a missing or unreadable preference
/// keeps the app offline. While it is off, the fields only that endpoint fills are served as null.
/// </summary>
public static class UsageApiPreference
{
    /// <summary>
    /// Whether the fallback may run. Read on every poll and every <c>/status</c> request rather
    /// than cached, so a toggle takes effect without restarting the listener.
    /// </summary>
    public static bool IsEnabled(string? path = null) =>
        FlagPreference.IsSet(path ?? VitalsPaths.UsageApiEnabledFlagFile);

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetEnabled(bool enabled, string? path = null) =>
        FlagPreference.TrySet(path ?? VitalsPaths.UsageApiEnabledFlagFile, enabled, "Usage API fallback enabled");
}
