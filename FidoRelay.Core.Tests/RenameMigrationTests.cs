using System.Text.Json.Nodes;
using FidoRelay.Core;

namespace FidoRelay.Core.Tests;

/// <summary>
/// Guards the upgrade path from the pre-rename "ClaudeVitals" install. These are the two places a
/// rename can break an existing user silently, so they stay covered even though the old name is
/// otherwise gone from the codebase.
/// </summary>
public class RenameMigrationTests : IDisposable
{
    private const string ExePath = @"C:\Users\Test User\AppData\Local\Programs\FidoRelay\FidoRelay.Hooks.exe";
    private const string LegacyExePath = @"C:\Users\Test User\AppData\Local\Programs\ClaudeVitals\ClaudeVitals.Hooks.exe";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "FidoRelayTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    public RenameMigrationTests() => Directory.CreateDirectory(_directory);

    private JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(SettingsPath))!;

    /// <summary>
    /// The failure this prevents: appending a second registration alongside the old one, leaving
    /// every event to also invoke a hook exe that no longer exists.
    /// </summary>
    [Fact]
    public void RepointsLegacyHookEntriesInsteadOfDuplicatingThem()
    {
        ClaudeSettingsMerger.Merge(LegacyExePath, SettingsPath);

        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.True(result.Changed);
        var settings = ReadSettings();

        foreach (var eventName in new[] { "Notification", "Stop", "UserPromptSubmit", "PreToolUse", "SessionStart", "SessionEnd" })
        {
            var entries = (JsonArray)settings["hooks"]![eventName]!;
            Assert.Single(entries);
            Assert.Contains(ExePath, entries[0]!["hooks"]![0]!["command"]!.GetValue<string>());
        }
    }

    [Fact]
    public void ReplacesALegacyStatusLineRatherThanWarningAboutIt()
    {
        ClaudeSettingsMerger.Merge(LegacyExePath, SettingsPath);

        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.Empty(result.Warnings);
        Assert.Contains(ExePath, ReadSettings()["statusLine"]!["command"]!.GetValue<string>());
    }

    /// <summary>A third-party statusLine must still be protected — the legacy check must not widen this.</summary>
    [Fact]
    public void StillLeavesAnUnrelatedStatusLineAlone()
    {
        File.WriteAllText(SettingsPath, """
        { "statusLine": { "type": "command", "command": "somebody-elses-tool.exe" } }
        """);

        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.Contains("somebody-elses-tool.exe", ReadSettings()["statusLine"]!["command"]!.GetValue<string>());
        Assert.NotEmpty(result.Warnings);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Temp directory; leaving it behind is harmless.
        }
    }
}
