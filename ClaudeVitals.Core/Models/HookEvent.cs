using System.Text.Json.Serialization;

namespace ClaudeVitals.Core.Models;

/// <summary>
/// The JSON Claude Code pipes to a lifecycle hook command on stdin (Notification, Stop,
/// PreToolUse, UserPromptSubmit). Only the fields this app cares about are modelled.
/// </summary>
public sealed record HookEvent
{
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("hook_event_name")]
    public string? HookEventName { get; init; }

    /// <summary>Set on PreToolUse/PostToolUse.</summary>
    [JsonPropertyName("tool_name")]
    public string? ToolName { get; init; }

    /// <summary>Set on Notification — the text shown to the user, e.g. a permission request.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
