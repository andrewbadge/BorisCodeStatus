using System.Drawing;
using BorisCodeStatus.Core.Models;

namespace BorisCodeStatus.Tray;

/// <summary>
/// What a <see cref="WaitingCard"/> shows. <paramref name="State"/> is the header on the right, in
/// <paramref name="Accent"/>. <paramref name="Dog"/> null draws the waiting portrait with its
/// blinking bang; a pose draws that tray sprite. <paramref name="Gauge"/> null makes the bar the
/// countdown; a percentage makes it the quota gauge.
/// </summary>
internal sealed record CardContent(
    string State,
    Color Accent,
    string Title,
    string Detail,
    string? Hint,
    DogState? Dog = null,
    double? Gauge = null)
{
    public static CardContent From(WaitingPrompt prompt) => new(
        "Waiting",
        DogSprites.Palette[DogSprites.BangIndex],
        prompt.Title,
        prompt.Detail,
        prompt.Hint);
}
