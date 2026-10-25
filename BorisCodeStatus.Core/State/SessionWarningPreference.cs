namespace BorisCodeStatus.Core.State;

/// <summary>
/// The 5-hour session usage, in percent, past which the tray shows a warning dialog.
///
/// A value preference like <see cref="ClickTargetPreference"/>: the default (<see cref="Default"/>)
/// is the file's absence, so a missing, unreadable or out-of-range file yields it and there is no
/// first-run write. Setting the default deletes the file rather than writing it. 100 is the
/// "never" setting — usage cannot pass it.
/// </summary>
public static class SessionWarningPreference
{
    public const int Default = 80;
    public const int Minimum = 50;
    public const int Maximum = 100;

    public static int Get(string? path = null)
    {
        try
        {
            return int.TryParse(File.ReadAllText(path ?? VitalsPaths.SessionWarningFile).Trim(), out var percent)
                && percent is >= Minimum and <= Maximum
                    ? percent
                    : Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Includes FileNotFound: the default.
            return Default;
        }
    }

    /// <summary>Records the threshold. Never throws, like the other preferences. Returns whether it was persisted.</summary>
    public static bool TrySet(int percent, string? path = null)
    {
        path ??= VitalsPaths.SessionWarningFile;
        try
        {
            if (percent == Default)
            {
                File.Delete(path);
            }
            else
            {
                VitalsPaths.EnsureDataDirectory();
                File.WriteAllText(path, Math.Clamp(percent, Minimum, Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
