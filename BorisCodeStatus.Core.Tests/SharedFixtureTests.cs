using System.Text.Json;
using System.Text.Json.Nodes;
using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Core.Tests;

/// <summary>
/// Runs the cases in the repo-root <c>fixtures/</c> folder, which the macOS build's Swift tests
/// run too. Two implementations of the pose rule and the hook folds can drift apart; holding both
/// to the same data is what stops that. Change a rule here and the fixture has to change, which
/// fails the Swift side until it agrees. See docs/macos-port.md.
/// </summary>
public class SharedFixtureTests
{
    // VitalsStateStore.JsonOptions is internal to Core, which grants no InternalsVisibleTo.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static TheoryData<string> DogStateCases() => CaseNames("dog-states.json");

    public static TheoryData<string> FoldCases() => CaseNames("folds.json");

    [Theory]
    [MemberData(nameof(DogStateCases))]
    public void PoseAndSessionStatusMatchTheFixture(string name)
    {
        var fixture = Load("dog-states.json");
        var now = DateTimeOffset.Parse(fixture["now"]!.GetValue<string>());
        var testCase = Case(fixture, name);
        var state = testCase["state"].Deserialize<VitalsState>(Options)!;

        Assert.Equal(testCase["session_status"]!.GetValue<string>(), state.SessionStatusAt(now).ToString());
        Assert.Equal(testCase["dog"]!.GetValue<string>(), DogStates.For(state, now).ToString());
    }

    [Theory]
    [MemberData(nameof(FoldCases))]
    public void HookSequenceFoldsToTheFixture(string name)
    {
        var testCase = Case(Load("folds.json"), name);
        var state = new VitalsState();

        foreach (var step in testCase["steps"]!.AsArray())
        {
            var verb = step!["verb"]!.GetValue<string>();
            var payloadNode = step["payload"];
            // A string payload is sent as-is, which is how the fixtures express malformed input.
            var payload = payloadNode is JsonValue value && value.TryGetValue<string>(out var raw)
                ? raw
                : payloadNode?.ToJsonString() ?? string.Empty;

            if (string.Equals(verb, "statusline", StringComparison.OrdinalIgnoreCase))
            {
                if (VitalsUpdates.TryParse<StatusLineEvent>(payload) is { } status)
                {
                    state = VitalsUpdates.ApplyStatusLine(state, status);
                }
            }
            else if (VitalsUpdates.ActivityForVerb(verb) is { } activity)
            {
                state = VitalsUpdates.ApplyActivity(state, activity, VitalsUpdates.TryParse<HookEvent>(payload));
            }
            else if (VitalsUpdates.SessionLifetimeForVerb(verb) is { } ended)
            {
                state = VitalsUpdates.ApplySessionLifetime(state, ended, VitalsUpdates.TryParse<HookEvent>(payload));
            }
        }

        var actual = new Dictionary<string, object?>
        {
            ["session_id"] = state.SessionId,
            ["session_name"] = state.SessionName,
            ["model_display_name"] = state.ModelDisplayName,
            ["session_cost_usd"] = state.SessionCostUsd,
            ["session_duration_ms"] = (double?)state.SessionDurationMs,
            ["context_used_percentage"] = state.ContextUsedPercentage,
            ["session_used_percentage"] = state.Session?.UsedPercentage,
            ["week_used_percentage"] = state.Week?.UsedPercentage,
            ["activity"] = state.Activity.ToString(),
            ["waiting_message"] = state.WaitingMessage,
            ["session_ended"] = state.SessionEndedUtc is not null,
        };

        foreach (var (field, expectedNode) in testCase["expected"]!.AsObject())
        {
            object? expected = expectedNode?.GetValueKind() switch
            {
                null or JsonValueKind.Null => null,
                JsonValueKind.Number => expectedNode.GetValue<double>(),
                JsonValueKind.True or JsonValueKind.False => expectedNode.GetValue<bool>(),
                _ => expectedNode.GetValue<string>(),
            };
            Assert.True(actual.ContainsKey(field), $"The fixture checks '{field}', which this test does not map.");
            Assert.Equal(expected, actual[field]);
        }
    }

    private static TheoryData<string> CaseNames(string file)
    {
        var data = new TheoryData<string>();
        foreach (var testCase in Load(file)["cases"]!.AsArray())
        {
            data.Add(testCase!["name"]!.GetValue<string>());
        }
        return data;
    }

    private static JsonNode Case(JsonNode fixture, string name) =>
        fixture["cases"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == name)!;

    private static JsonNode Load(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(FixturesDirectory(), file)))!;

    /// <summary>Walks up from the test binaries to the repo root, which holds <c>fixtures/</c>.</summary>
    private static string FixturesDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "fixtures");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repo-root fixtures folder.");
    }
}
