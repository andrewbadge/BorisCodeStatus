using System.Globalization;
using FidoRelay.Core.Models;
using FidoRelay.Core.State;

namespace FidoRelay.Hooks;

/// <summary>
/// The single executable Claude Code invokes for both statusLine and lifecycle hooks.
/// Which one it is comes from the verb argument each registration passes
/// (<c>FidoRelay.Hooks.exe statusline</c>, <c>... notification</c>, and so on).
///
/// Design constraints, both from Claude Code's side:
///  * It must be fast. Claude Code cancels an in-flight statusLine script when the next event
///    arrives, and a slow script stalls status line updates. So: file I/O only, never network.
///  * It must never fail loudly. A non-zero exit or a hang is worse than a missed update.
/// </summary>
internal static class Program
{
    /// <summary>Hard ceiling on the whole run; if anything blocks, abandon the update and leave.</summary>
    private static readonly TimeSpan Watchdog = TimeSpan.FromSeconds(3);

    private static int Main(string[] args)
    {
        // Belt and braces against a blocked stdin read or a contended file lock: this process
        // must never become the reason Claude Code's status line stops updating.
        using var watchdog = new Timer(_ => Environment.Exit(0), null, Watchdog, Timeout.InfiniteTimeSpan);

        try
        {
            var verb = args.Length > 0 ? args[0] : string.Empty;
            var payload = ReadStandardInput();

            if (string.Equals(verb, "statusline", StringComparison.OrdinalIgnoreCase))
            {
                Console.Out.Write(HandleStatusLine(payload));
                return 0;
            }

            if (VitalsUpdates.ActivityForVerb(verb) is { } activity)
            {
                HandleLifecycle(payload, activity);
                return 0;
            }

            if (VitalsUpdates.SessionLifetimeForVerb(verb) is { } ended)
            {
                HandleSessionLifetime(payload, ended);
                return 0;
            }

            // Unknown verb: say so on stderr (which Claude Code surfaces in debug output only)
            // and still succeed, so a stale registration never blocks a session.
            Console.Error.WriteLine($"FidoRelay: unrecognised verb '{verb}'.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FidoRelay: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Records usage data and returns the text Claude Code renders as the status line.
    /// We occupy the user's statusLine slot, so returning something useful is better manners
    /// than returning nothing.
    /// </summary>
    private static string HandleStatusLine(string payload)
    {
        var status = VitalsUpdates.TryParse<StatusLineEvent>(payload);
        if (status is null)
        {
            return string.Empty;
        }

        var state = VitalsStateStore.Default.Update(current => VitalsUpdates.ApplyStatusLine(current, status));
        return Render(state);
    }

    private static void HandleLifecycle(string payload, ActivityState activity)
    {
        var hook = VitalsUpdates.TryParse<HookEvent>(payload);
        VitalsStateStore.Default.Update(current => VitalsUpdates.ApplyActivity(current, activity, hook));
    }

    private static void HandleSessionLifetime(string payload, bool ended)
    {
        var hook = VitalsUpdates.TryParse<HookEvent>(payload);
        VitalsStateStore.Default.Update(current => VitalsUpdates.ApplySessionLifetime(current, ended, hook));
    }

    private static string Render(VitalsState state)
    {
        var parts = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(state.ModelDisplayName))
        {
            parts.Add(state.ModelDisplayName);
        }

        if (state.Session?.UsedPercentage is { } session)
        {
            parts.Add($"5h {session.ToString("0", CultureInfo.InvariantCulture)}%");
        }

        if (state.Week?.UsedPercentage is { } week)
        {
            parts.Add($"7d {week.ToString("0", CultureInfo.InvariantCulture)}%");
        }

        if (state.ContextUsedPercentage is { } context)
        {
            parts.Add($"ctx {context.ToString("0", CultureInfo.InvariantCulture)}%");
        }

        return string.Join("  ", parts);
    }

    /// <summary>
    /// Reads the whole payload from stdin. Returns empty when stdin is a console rather than a
    /// pipe, so running the exe by hand does not hang waiting for input that will never come.
    /// </summary>
    private static string ReadStandardInput()
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadToEnd();
        }

        return string.Empty;
    }
}
