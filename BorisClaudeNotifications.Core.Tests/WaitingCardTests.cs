using BorisClaudeNotifications.Tray;

namespace BorisClaudeNotifications.Core.Tests;

/// <summary>
/// Covers what the waiting card says and the data it draws from. The window itself is not tested:
/// it is paint code, and what can go wrong in it is visible the first time it appears.
/// </summary>
public class WaitingCardTests
{
    [Fact]
    public void PermissionPromptGetsItsHeadingAndTheAnswerHint()
    {
        var prompt = WaitingPrompt.From("Claude needs your permission to use Bash");

        Assert.Equal("Permission needed", prompt.Title);
        Assert.Equal("Claude needs your permission to use Bash", prompt.Detail);
        Assert.Equal("Y / N", prompt.Hint);
    }

    [Fact]
    public void IdlePromptIsInputNeededWithNoHint()
    {
        var prompt = WaitingPrompt.From("Claude is waiting for your input");

        Assert.Equal("Input needed", prompt.Title);
        Assert.Null(prompt.Hint);
    }

    /// <summary>The phrases are not a contract, so unknown text must still produce a true heading.</summary>
    [Fact]
    public void UnrecognisedMessageFallsBackToAGeneralHeading()
    {
        var prompt = WaitingPrompt.From("Something new Claude Code has started sending");

        Assert.Equal("Claude needs you", prompt.Title);
        Assert.Equal("Something new Claude Code has started sending", prompt.Detail);
        Assert.Null(prompt.Hint);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingMessageNeverLeavesTheCardBlank(string? message)
    {
        var prompt = WaitingPrompt.From(message);

        Assert.Equal(WaitingPrompt.FallbackDetail, prompt.Detail);
    }

    [Fact]
    public void PortraitIsRectangularAndInPalette()
    {
        var rows = DogSprites.WaitingPortrait;
        Assert.All(rows, row => Assert.Equal(rows[0].Length, row.Length));
        Assert.All(
            rows.SelectMany(row => row),
            cell => Assert.InRange(DogSprites.IndexOf(cell), 0, DogSprites.Palette.Length - 1));
    }

    /// <summary>
    /// The card blinks the bang by skipping its palette index, so that index must be the bang and
    /// nothing else — if the tail or an eye used it, they would blink too. The bang sits to the
    /// right of the head, clear of the dog's own columns in the top rows.
    /// </summary>
    [Fact]
    public void BangIndexIsUsedOnlyByTheBang()
    {
        var cells = DogSprites.WaitingPortrait
            .SelectMany((row, y) => row.Select((cell, x) => (x, y, cell)))
            .Where(c => DogSprites.IndexOf(c.cell) == DogSprites.BangIndex)
            .ToList();

        Assert.NotEmpty(cells);
        Assert.All(cells, c => Assert.True(c.x >= 20 && c.y <= 5, $"bang colour at ({c.x},{c.y})"));
    }

    [Fact]
    public void PixelFontWrapsTheDesignHeadingOntoTwoLines()
    {
        // At the design's 1× the text column is 146px, and "PERMISSION NEEDED" is set on two lines.
        var lines = PixelFont.Wrap("PERMISSION NEEDED", maxWidth: 146, pixel: 1, tracking: 3, bold: true);

        Assert.Equal(["PERMISSION", "NEEDED"], lines);
    }
}
