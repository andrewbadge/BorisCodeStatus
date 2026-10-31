namespace BorisCodeStatus.Core.State;

/// <summary>
/// Which app, if any, clicking the waiting card brings to the front — stored as its process name
/// (e.g. <c>WindowsTerminal</c>, <c>chrome</c>). The default is that a click only dismisses, so a
/// missing, empty or unreadable setting yields null.
/// </summary>
public static class ClickTargetPreference
{
    /// <summary>The chosen process name, or null to only dismiss the card.</summary>
    public static string? Get(string? path = null) =>
        SettingsStore.Load(path).ClickTargetApp is { } name && !string.IsNullOrWhiteSpace(name) ? name.Trim() : null;

    /// <summary>Records the choice, or clears it with null. Returns whether it was persisted.</summary>
    public static bool TrySet(string? processName, string? path = null) =>
        SettingsStore.TryUpdate(
            s => s with { ClickTargetApp = string.IsNullOrWhiteSpace(processName) ? null : processName.Trim() },
            path);
}
