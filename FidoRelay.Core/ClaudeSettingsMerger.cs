using System.Text.Json;
using System.Text.Json.Nodes;
using FidoRelay.Core.State;

namespace FidoRelay.Core;

/// <summary>Outcome of a settings merge, so the caller can surface conflicts instead of hiding them.</summary>
public sealed record SettingsMergeResult(bool Changed, IReadOnlyList<string> Warnings)
{
    public static SettingsMergeResult NoChange { get; } = new(false, []);
}

/// <summary>
/// Registers this app's statusLine and lifecycle hooks in ~/.claude/settings.json.
///
/// The file is read-modify-written as a JSON tree, never regenerated: it routinely holds
/// unrelated user configuration (permissions, model, theme) that must survive untouched.
/// An existing third-party <c>statusLine</c> is left alone and reported as a warning rather
/// than clobbered — losing someone's status line to an installer would be unacceptable.
/// </summary>
public static class ClaudeSettingsMerger
{
    private const string HooksExeName = "FidoRelay.Hooks.exe";

    /// <summary>
    /// What the hook executable was called before the rename to FidoRelay. Still recognised as
    /// ours: an upgraded install must <em>repoint</em> the old entries, not leave them behind and
    /// append a second set. Duplicates would run a now-missing exe on every event — silently, since
    /// the hook is built never to report errors — and the statusLine slot can only hold one command.
    /// </summary>
    private const string LegacyHooksExeName = "ClaudeVitals.Hooks.exe";

    /// <summary>Lifecycle hooks to register, as (settings key, verb argument, matcher).</summary>
    private static readonly (string EventName, string Verb, string? Matcher)[] LifecycleHooks =
    [
        ("Notification", "notification", null),
        ("Stop", "stop", null),
        ("UserPromptSubmit", "userpromptsubmit", null),
        ("PreToolUse", "pretooluse", "*"),
        ("SessionStart", "sessionstart", null),
        ("SessionEnd", "sessionend", null),
    ];

    /// <summary>
    /// Ensures the hook entries point at <paramref name="hooksExePath"/>. Idempotent: running it
    /// again after an upgrade rewrites stale paths but adds nothing. Returns whether the file changed.
    /// </summary>
    public static SettingsMergeResult Merge(string hooksExePath, string? settingsPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hooksExePath);

        var path = settingsPath ?? VitalsPaths.SettingsFile;
        var warnings = new List<string>();

        JsonObject root;
        var existed = File.Exists(path);
        if (existed)
        {
            var text = File.ReadAllText(path);
            try
            {
                root = string.IsNullOrWhiteSpace(text)
                    ? new JsonObject()
                    : JsonNode.Parse(text) as JsonObject ?? new JsonObject();
            }
            catch (JsonException ex)
            {
                // Refuse to touch a file we cannot parse; overwriting it would destroy user config.
                return new SettingsMergeResult(false, [$"Could not parse {path} ({ex.Message}); left unchanged."]);
            }
        }
        else
        {
            root = new JsonObject();
        }

        var before = root.ToJsonString();

        MergeStatusLine(root, hooksExePath, warnings);
        MergeLifecycleHooks(root, hooksExePath);

        if (root.ToJsonString() == before && existed)
        {
            return new SettingsMergeResult(false, warnings);
        }

        WriteAtomically(path, root, backup: existed);
        return new SettingsMergeResult(true, warnings);
    }

    private static void MergeStatusLine(JsonObject root, string hooksExePath, List<string> warnings)
    {
        var desired = new JsonObject
        {
            ["type"] = "command",
            ["command"] = BuildCommand(hooksExePath, "statusline"),
        };

        if (root["statusLine"] is JsonObject existing)
        {
            var command = existing["command"]?.GetValue<string>();
            if (!IsOurs(command))
            {
                warnings.Add(
                    "An existing statusLine command was left in place; FidoRelay will not receive " +
                    "usage data until it is replaced. Remove the 'statusLine' entry from settings.json to switch.");
                return;
            }
        }

        root["statusLine"] = desired;
    }

    private static void MergeLifecycleHooks(JsonObject root, string hooksExePath)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = [];
            root["hooks"] = hooks;
        }

        foreach (var (eventName, verb, matcher) in LifecycleHooks)
        {
            if (hooks[eventName] is not JsonArray matchers)
            {
                matchers = [];
                hooks[eventName] = matchers;
            }

            var command = BuildCommand(hooksExePath, verb);
            if (TryUpdateExistingEntry(matchers, command))
            {
                continue;
            }

            var entry = new JsonObject();
            if (matcher is not null)
            {
                entry["matcher"] = matcher;
            }

            entry["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
            });

            matchers.Add(entry);
        }
    }

    /// <summary>Rewrites our own entry in place if present (handles an upgrade to a new path).</summary>
    private static bool TryUpdateExistingEntry(JsonArray matchers, string command)
    {
        foreach (var matcherNode in matchers)
        {
            if (matcherNode is not JsonObject matcherObject || matcherObject["hooks"] is not JsonArray inner)
            {
                continue;
            }

            foreach (var hookNode in inner)
            {
                if (hookNode is not JsonObject hookObject)
                {
                    continue;
                }

                if (IsOurs(hookObject["command"]?.GetValue<string>()))
                {
                    hookObject["command"] = command;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsOurs(string? command) =>
        command?.Contains(HooksExeName, StringComparison.OrdinalIgnoreCase) == true ||
        command?.Contains(LegacyHooksExeName, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Quotes the path: the default install directory sits under a user profile that may contain spaces.</summary>
    private static string BuildCommand(string hooksExePath, string verb) => $"\"{hooksExePath}\" {verb}";

    private static void WriteAtomically(string path, JsonObject root, bool backup)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (backup)
        {
            // One-time safety net; never overwritten, so the pre-FidoRelay file is always recoverable.
            var backupPath = path + ".fidorelay.bak";
            if (!File.Exists(backupPath))
            {
                File.Copy(path, backupPath);
            }
        }

        var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }
}
