namespace FidoRelay.Core.State;

/// <summary>
/// Remembers that the user paused the HTTP service, so it stays paused across a restart —
/// including the automatic one at login. Default is running, so the marker file means "paused".
/// </summary>
public static class PausePreference
{
    /// <summary>Whether the service should start paused.</summary>
    public static bool IsPaused(string? path = null) =>
        FlagPreference.IsSet(path ?? VitalsPaths.PausedFlagFile);

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySet(bool paused, string? path = null) =>
        FlagPreference.TrySet(path ?? VitalsPaths.PausedFlagFile, paused, "Paused");
}
