using System.Globalization;

namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the status card stays on screen, and where the user last dragged it.
///
/// Off by default, so the marker file means pinned. The position is a separate file holding
/// "x,y" in screen pixels; a missing or garbled one just means the default corner. It is stored
/// as it was left and only kept on screen when it is used, because the monitors present at
/// that point are the ones that matter.
/// </summary>
public static class StatusCardPreference
{
    public static bool IsPinned(string? path = null) =>
        FlagPreference.IsSet(path ?? VitalsPaths.StatusCardPinnedFlagFile);

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetPinned(bool pinned, string? path = null) =>
        FlagPreference.TrySet(
            path ?? VitalsPaths.StatusCardPinnedFlagFile,
            set: pinned,
            reason: "Status card pinned");

    /// <summary>Whether the status card is the mini one. Off by default, so the marker means mini.</summary>
    public static bool IsMini(string? path = null) =>
        FlagPreference.IsSet(path ?? VitalsPaths.StatusCardMiniFlagFile);

    public static bool TrySetMini(bool mini, string? path = null) =>
        FlagPreference.TrySet(path ?? VitalsPaths.StatusCardMiniFlagFile, set: mini, reason: "Mini status card chosen");

    /// <summary>The last saved top-left corner, or null if there is none or it cannot be read.</summary>
    public static (int X, int Y)? GetPosition(string? path = null)
    {
        try
        {
            var parts = File.ReadAllText(path ?? VitalsPaths.StatusCardPositionFile).Split(',');
            return parts.Length == 2
                && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                ? (x, y)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves the top-left corner. Never throws; returns whether it was persisted.</summary>
    public static bool TrySavePosition(int x, int y, string? path = null)
    {
        try
        {
            VitalsPaths.EnsureDataDirectory();
            File.WriteAllText(
                path ?? VitalsPaths.StatusCardPositionFile,
                string.Create(CultureInfo.InvariantCulture, $"{x},{y}"));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
