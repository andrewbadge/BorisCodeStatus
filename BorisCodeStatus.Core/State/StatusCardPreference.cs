namespace BorisCodeStatus.Core.State;

/// <summary>
/// Whether the status card stays on screen, whether it is the mini one, and where the user last
/// dragged it. All off or absent by default. The position is stored as it was left and only kept
/// on screen when it is used, because the monitors present at that point are the ones that matter.
/// </summary>
public static class StatusCardPreference
{
    public static bool IsPinned(string? path = null) => SettingsStore.Load(path).StatusCardPinned;

    /// <summary>Records the preference. Returns whether it was persisted.</summary>
    public static bool TrySetPinned(bool pinned, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { StatusCardPinned = pinned }, path);

    public static bool IsMini(string? path = null) => SettingsStore.Load(path).StatusCardMini;

    public static bool TrySetMini(bool mini, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { StatusCardMini = mini }, path);

    /// <summary>The last saved top-left corner, or null if there is none.</summary>
    public static (int X, int Y)? GetPosition(string? path = null) =>
        SettingsStore.Load(path).StatusCardPosition is { } position ? (position.X, position.Y) : null;

    /// <summary>Saves the top-left corner. Returns whether it was persisted.</summary>
    public static bool TrySavePosition(int x, int y, string? path = null) =>
        SettingsStore.TryUpdate(s => s with { StatusCardPosition = new CardPosition(x, y) }, path);
}
