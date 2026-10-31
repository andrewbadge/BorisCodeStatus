namespace BorisCodeStatus.Tray;

/// <summary>What the waiting card says, derived from the Notification hook's text.</summary>
/// <param name="Title">Pixel-font heading, e.g. "PERMISSION NEEDED".</param>
/// <param name="Detail">The hook's own sentence, shown as-is beneath the heading.</param>
/// <param name="Hint">Short label beside the bar ("Y / N"), or null for none.</param>
internal sealed record WaitingPrompt(string Title, string Detail, string? Hint)
{
    public const string FallbackDetail = "Claude Code is waiting for your input.";

    /// <summary>
    /// Classifies by the message text because that is all the hook payload is known to carry: the
    /// Notification event's documented field is <c>message</c>. The phrases matched are the ones
    /// Claude Code has been seen to send ("Claude needs your permission to use Bash", "Claude is
    /// waiting for your input"); they are not a contract, so anything unrecognised falls through
    /// to a heading that is true of every Notification rather than a guess.
    /// </summary>
    public static WaitingPrompt From(string? message)
    {
        var detail = string.IsNullOrWhiteSpace(message) ? FallbackDetail : message.Trim();

        if (detail.Contains("permission", StringComparison.OrdinalIgnoreCase))
        {
            // The answer is given in the terminal, not on the card — the hint says what Claude is
            // asking for, the way the design's card does, and the card itself takes no input.
            return new WaitingPrompt("Permission needed", detail, "Y / N");
        }

        if (detail.Contains("waiting for your input", StringComparison.OrdinalIgnoreCase))
        {
            return new WaitingPrompt("Input needed", detail, null);
        }

        return new WaitingPrompt("Claude needs you", detail, null);
    }
}
