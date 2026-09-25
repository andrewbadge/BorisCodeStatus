namespace BorisClaudeNotifications.Core.Models;

/// <summary>
/// What the Claude Code session is currently doing, derived from lifecycle hooks
/// (not from statusLine, which carries no lifecycle signal).
/// </summary>
public enum ActivityState
{
    /// <summary>No lifecycle hook has fired yet in this install.</summary>
    Unknown = 0,

    /// <summary>A turn finished (Stop hook).</summary>
    Idle = 1,

    /// <summary>A prompt was submitted or a tool is about to run (UserPromptSubmit / PreToolUse).</summary>
    Working = 2,

    /// <summary>Claude Code is blocked on the user, e.g. a permission prompt (Notification hook).</summary>
    Waiting = 3,
}
