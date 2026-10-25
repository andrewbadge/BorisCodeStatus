namespace BorisCodeStatus.Core.State;

/// <summary>
/// The 5-hour session usage, in percent, past which the tray shows its warning card. 100 is the
/// "never" setting — usage cannot pass it. A missing, unreadable or out-of-range value yields
/// <see cref="Default"/>.
/// </summary>
public static class SessionWarningPreference
{
    public const int Default = 80;
    public const int Minimum = 50;
    public const int Maximum = 100;

    public static int Get(string? path = null)
    {
        var percent = SettingsStore.Load(path).SessionWarningPercent;
        return percent is >= Minimum and <= Maximum ? percent : Default;
    }

    /// <summary>Records the threshold. Returns whether it was persisted.</summary>
    public static bool TrySet(int percent, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { SessionWarningPercent = Math.Clamp(percent, Minimum, Maximum) }, path);
}
