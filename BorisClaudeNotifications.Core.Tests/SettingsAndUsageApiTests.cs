using System.Text.Json.Nodes;
using BorisClaudeNotifications.Core;

namespace BorisClaudeNotifications.Core.Tests;

/// <summary>
/// Guards the riskiest thing this app does: editing a file the user owns and relies on. Every test
/// here exists because the failure it describes would damage a live Claude Code configuration.
/// </summary>
public class ClaudeSettingsMergerTests : IDisposable
{
    private const string ExePath = @"C:\Users\Test User\AppData\Local\Programs\BorisClaudeNotifications\BorisClaudeNotifications.Hooks.exe";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisClaudeNotificationsTests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_directory, "settings.json");


    public ClaudeSettingsMergerTests() => Directory.CreateDirectory(_directory);

    private JsonObject ReadSettings() => (JsonObject)JsonNode.Parse(File.ReadAllText(SettingsPath))!;

    [Fact]
    public void CreatesSettingsWhenNoneExists()
    {
        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.True(result.Changed);
        var settings = ReadSettings();
        Assert.Equal("command", settings["statusLine"]!["type"]!.GetValue<string>());
        Assert.Contains("statusline", settings["statusLine"]!["command"]!.GetValue<string>());
        Assert.NotNull(settings["hooks"]!["Notification"]);
        Assert.NotNull(settings["hooks"]!["Stop"]);
    }

    [Fact]
    public void QuotesTheExecutablePathBecauseItContainsSpaces()
    {
        ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        var command = ReadSettings()["statusLine"]!["command"]!.GetValue<string>();

        Assert.Equal($"\"{ExePath}\" statusline", command);
    }

    [Fact]
    public void PreservesUnrelatedUserConfiguration()
    {
        File.WriteAllText(SettingsPath, """
        {
          "theme": "dark",
          "permissions": { "allow": ["Bash(git status)"] },
          "env": { "FOO": "bar" }
        }
        """);

        ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        var settings = ReadSettings();
        Assert.Equal("dark", settings["theme"]!.GetValue<string>());
        Assert.Equal("Bash(git status)", settings["permissions"]!["allow"]![0]!.GetValue<string>());
        Assert.Equal("bar", settings["env"]!["FOO"]!.GetValue<string>());
        Assert.NotNull(settings["statusLine"]);
    }

    [Fact]
    public void IsIdempotent()
    {
        ClaudeSettingsMerger.Merge(ExePath, SettingsPath);
        var afterFirst = File.ReadAllText(SettingsPath);

        var second = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.False(second.Changed);
        Assert.Equal(afterFirst, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void RewritesOurOwnCommandAfterAnUpgradeChangesThePath()
    {
        ClaudeSettingsMerger.Merge(@"C:\Old\BorisClaudeNotifications.Hooks.exe", SettingsPath);

        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.True(result.Changed);
        var settings = ReadSettings();
        Assert.Contains(ExePath, settings["statusLine"]!["command"]!.GetValue<string>());

        var notificationCommand = settings["hooks"]!["Notification"]![0]!["hooks"]![0]!["command"]!.GetValue<string>();
        Assert.Contains(ExePath, notificationCommand);
        Assert.Single((JsonArray)settings["hooks"]!["Notification"]!);
    }

    [Fact]
    public void LeavesAThirdPartyStatusLineAloneAndWarns()
    {
        File.WriteAllText(SettingsPath, """
        { "statusLine": { "type": "command", "command": "my-own-statusline.sh" } }
        """);

        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.Equal("my-own-statusline.sh", ReadSettings()["statusLine"]!["command"]!.GetValue<string>());
        Assert.Contains(result.Warnings, w => w.Contains("statusLine"));
    }

    [Fact]
    public void AppendsAlongsideExistingUnrelatedHooks()
    {
        File.WriteAllText(SettingsPath, """
        {
          "hooks": {
            "Stop": [ { "hooks": [ { "type": "command", "command": "notify-send done" } ] } ]
          }
        }
        """);

        ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        var stop = (JsonArray)ReadSettings()["hooks"]!["Stop"]!;
        Assert.Equal(2, stop.Count);
        Assert.Equal("notify-send done", stop[0]!["hooks"]![0]!["command"]!.GetValue<string>());
    }

    [Fact]
    public void BacksUpAnExistingFileOnce()
    {
        File.WriteAllText(SettingsPath, """{ "theme": "dark" }""");

        ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        var backup = SettingsPath + ".borisclaudenotifications.bak";
        Assert.True(File.Exists(backup));
        Assert.Contains("dark", File.ReadAllText(backup));
        Assert.DoesNotContain("statusLine", File.ReadAllText(backup));
    }

    [Fact]
    public void RefusesToTouchAnUnparseableFile()
    {
        const string garbage = "{ definitely not json";
        File.WriteAllText(SettingsPath, garbage);

        var result = ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        Assert.False(result.Changed);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(garbage, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void PreToolUseGetsAWildcardMatcher()
    {
        ClaudeSettingsMerger.Merge(ExePath, SettingsPath);

        var preToolUse = (JsonArray)ReadSettings()["hooks"]!["PreToolUse"]!;

        Assert.Equal("*", preToolUse[0]!["matcher"]!.GetValue<string>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only.
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Covers credential reading, the defensive response parsing, and above all the throttle — the
/// fallback endpoint punishes frequent polling, so the interval floor is treated as a contract.
/// </summary>
public class UsageApiClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "BorisClaudeNotificationsTests", Guid.NewGuid().ToString("N"));

    public UsageApiClientTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ReadsOauthTokenFromCredentialsFile()
    {
        var path = Path.Combine(_directory, ".credentials.json");
        File.WriteAllText(path, """
        { "claudeAiOauth": { "accessToken": "sk-ant-oat01-example", "expiresAt": 1789000000 } }
        """);

        Assert.Equal("sk-ant-oat01-example", UsageApiClient.TryReadAccessToken(path));
    }

    [Fact]
    public void MissingOrMalformedCredentialsYieldNull()
    {
        Assert.Null(UsageApiClient.TryReadAccessToken(Path.Combine(_directory, "absent.json")));

        var malformed = Path.Combine(_directory, "bad.json");
        File.WriteAllText(malformed, "{ nope");
        Assert.Null(UsageApiClient.TryReadAccessToken(malformed));

        var wrongShape = Path.Combine(_directory, "other.json");
        File.WriteAllText(wrongShape, """{ "somethingElse": true }""");
        Assert.Null(UsageApiClient.TryReadAccessToken(wrongShape));
    }

    [Fact]
    public void ExtractsWindowUsingWhicheverKeyTheServerSends()
    {
        const string json = """
        {
          "five_hour": { "utilization": 10, "resets_at": "2026-09-13T19:00:00Z" },
          "seven_day_sonnet": { "utilization": 44, "resets_at": "2026-09-17T04:30:00Z" }
        }
        """;

        var window = UsageApiClient.ExtractWindow(json, "seven_day_sonnet", "sonnet_seven_day");

        Assert.Equal(44, window?.UsedPercentage);
        Assert.Equal(new DateTimeOffset(2026, 9, 17, 4, 30, 0, TimeSpan.Zero), window?.ResetsAt);
    }

    [Fact]
    public void ExtractWindowAcceptsAlternateFieldNames()
    {
        const string json = """{ "seven_day_sonnet": { "used_percentage": 7, "resetsAt": 1789000000 } }""";

        var window = UsageApiClient.ExtractWindow(json, "seven_day_sonnet");

        Assert.Equal(7, window?.UsedPercentage);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1789000000), window?.ResetsAt);
    }

    [Fact]
    public void ExtractWindowReturnsNullWhenAbsentOrUnparseable()
    {
        Assert.Null(UsageApiClient.ExtractWindow("""{ "five_hour": { "utilization": 1 } }""", "seven_day_sonnet"));
        Assert.Null(UsageApiClient.ExtractWindow("not json", "seven_day_sonnet"));
        Assert.Null(UsageApiClient.ExtractWindow("[]", "seven_day_sonnet"));
    }

    [Fact]
    public void MinimumIntervalIsNotLoweredBelowFiveMinutes()
    {
        // Guards the documented constraint: this endpoint rate-limits hard and stays limited.
        Assert.True(UsageApiClient.MinimumInterval >= TimeSpan.FromMinutes(5));
        Assert.True(UsageApiClient.RateLimitedBackoff > UsageApiClient.MinimumInterval);
    }

    [Fact]
    public async Task ThrottleSuppressesASecondCallWithoutNetworkAccess()
    {
        var stamp = Path.Combine(_directory, "usage-api.stamp");
        File.WriteAllText(stamp, DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));

        // No HttpClient handler is configured, so a call that got past the throttle would fail loudly.
        using var client = new UsageApiClient(new HttpClient(new ThrowingHandler()), stamp);

        Assert.Null(await client.TryGetSonnetWeekAsync(CancellationToken.None));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Throttle should have prevented this call.");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup only.
        }

        GC.SuppressFinalize(this);
    }
}
