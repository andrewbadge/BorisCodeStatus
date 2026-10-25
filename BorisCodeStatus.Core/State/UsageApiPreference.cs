namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the relay may call the undocumented <c>/api/oauth/usage</c> endpoint for the
/// Sonnet-only weekly figure.
///
/// Off by default: it is the only thing in the app that reads the Claude Code OAuth token or
/// talks to the network, for one figure most displays do not show, from an endpoint that is not a
/// contract. So a missing or unreadable settings file keeps the app offline. While it is off, the
/// fields only that endpoint fills are served as null.
/// </summary>
public static class UsageApiPreference
{
    /// <summary>
    /// Whether the fallback may run. Read on every poll and every <c>/status</c> request, so a
    /// toggle takes effect without restarting the listener.
    /// </summary>
    public static bool IsEnabled(string? path = null) => SettingsStore.Load(path).UsageApiEnabled;

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetEnabled(bool enabled, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { UsageApiEnabled = enabled }, path);
}
