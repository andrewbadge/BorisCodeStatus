namespace BorisCodeStatus.Core.State;

/// <summary>
/// Which app, if any, clicking the waiting card brings to the front — stored as its process name
/// (e.g. <c>WindowsTerminal</c>, <c>chrome</c>).
///
/// The one preference that carries a value rather than a yes/no, so the file's content is read
/// back, unlike <see cref="FlagPreference"/>. The same convention holds otherwise: the default —
/// clicking only dismisses — is the file's absence, so a missing, empty or unreadable file yields
/// it and there is no first-run write.
/// </summary>
public static class ClickTargetPreference
{
    /// <summary>The chosen process name, or null to only dismiss the card.</summary>
    public static string? Get(string? path = null)
    {
        try
        {
            var name = File.ReadAllText(path ?? VitalsPaths.ClickTargetFile).Trim();
            return name.Length == 0 ? null : name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Includes FileNotFound: the default.
            return null;
        }
    }

    /// <summary>
    /// Records the choice, or clears it with null. Never throws, for the same reason as
    /// <see cref="FlagPreference.TrySet"/>. Returns whether it was persisted.
    /// </summary>
    public static bool TrySet(string? processName, string? path = null)
    {
        path ??= VitalsPaths.ClickTargetFile;
        try
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                File.Delete(path);
            }
            else
            {
                VitalsPaths.EnsureDataDirectory();
                File.WriteAllText(path, processName.Trim());
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
