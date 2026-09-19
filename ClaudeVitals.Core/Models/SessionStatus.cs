namespace ClaudeVitals.Core.Models;

/// <summary>
/// Whether a Claude Code session is open at all, independent of what it is doing right now.
///
/// This exists because <see cref="ActivityState"/> answers a narrower question than it looks
/// like it does: <c>Idle</c> is set by the Stop hook the moment a turn finishes, so a session
/// the user is actively chatting in reads as <c>Idle</c> for most of its wall-clock life —
/// all the time spent reading a reply and typing the next prompt. A display that wants to show
/// "a session is open" rather than "Claude is generating right now" needs this instead.
/// </summary>
public enum SessionStatus
{
    /// <summary>No session event has been seen yet in this install.</summary>
    Unknown = 0,

    /// <summary>A session is open: an event arrived recently and no SessionEnd has followed it.</summary>
    Active = 1,

    /// <summary>
    /// No session event for longer than the idle timeout. Inferred, not observed — it is what
    /// a session that died without firing SessionEnd (killed terminal, crash) decays into.
    /// </summary>
    Inactive = 2,

    /// <summary>The SessionEnd hook fired — the session closed cleanly.</summary>
    Ended = 3,
}
