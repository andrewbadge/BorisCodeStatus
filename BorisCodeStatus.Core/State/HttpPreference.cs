namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the tray should run the HTTP service, remembered across a restart — including the
/// automatic one at login.
///
/// Off by default: the endpoint is unauthenticated and serves session names and cost to the whole
/// LAN, so it should be something the user switches on, not something they have to know to switch
/// off. A missing or unreadable settings file therefore leaves the endpoint closed.
/// </summary>
public static class HttpPreference
{
    /// <summary>Whether the service should start listening.</summary>
    public static bool IsEnabled(string? path = null) => SettingsStore.Load(path).HttpEnabled;

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetEnabled(bool enabled, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { HttpEnabled = enabled }, path);
}
