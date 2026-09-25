using BorisCodeStatus.Api;
using BorisCodeStatus.Core;
using BorisCodeStatus.Core.State;
using Microsoft.AspNetCore.Builder;

namespace BorisCodeStatus.Tray;

/// <summary>
/// Entry point for the user-mode relay. One process does everything: it hosts the ESP32-facing
/// HTTP endpoint in-process (no second executable to install or supervise) and owns the tray icon
/// on the same message loop.
/// </summary>
internal static class Program
{
    // Per-user, not global: the app is a per-user install and two different users signed into the
    // same machine legitimately run their own copies.
    private const string SingleInstanceMutexName = @"Local\BorisCodeStatus.Tray.singleton";

    [STAThread]
    private static void Main()
    {
        using var singleton = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isOnlyInstance);
        if (!isOnlyInstance)
        {
            // A second copy would fail to bind the port anyway; leaving quietly is the right
            // behaviour for something launched from the Run key and possibly also by hand.
            return;
        }

        ApplicationConfiguration.Initialize();

        var options = VitalsApiOptions.FromEnvironment();
        var store = VitalsStateStore.Default;

        // Off unless the user has switched it on. The endpoint is unauthenticated and LAN-wide, so a
        // fresh install should not open a port nobody asked for; once switched on it stays on
        // across restarts, including the automatic one at login.
        var startListening = HttpPreference.IsEnabled();

        // Deliberately before StartApi: binding a non-loopback port is what raises the Windows
        // firewall prompt, and a user who meets that prompt with no context tends to dismiss it —
        // which does not skip the rule, it blocks the app. Explain first, then bind. Skipped
        // entirely when the service is off, since nothing is about to bind; the tray shows the
        // notice instead when the user first switches it on.
        if (startListening)
        {
            FirewallGuard.ShowFirstRunNoticeIfNeeded(options.Port);
        }

        using var api = new VitalsApiHost(options, store);
        try
        {
            if (startListening)
            {
                api.Start();
            }
        }
        catch (Exception ex)
        {
            // Most likely the port is already taken. The tray is still worth running: the state
            // file and the icon work without the listener, and the message says what went wrong.
            MessageBox.Show(
                $"The status endpoint could not start on port {options.Port}.\r\n\r\n{ex.Message}\r\n\r\n" +
                $"Set the {VitalsApiOptions.PortEnvironmentVariable} environment variable to use a different port.",
                "BorisCodeStatus",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        using var tray = new TrayIcon(store, options.Port, api);

        // Registering hooks is done here rather than in an MSI custom action — see README. It runs
        // after the tray is up so a locked or unusual settings.json never delays or fails startup.
        EnsureHooksRegistered();

        Application.Run();

        // Disposing the host stops the listener; `using` on the declaration above would only run
        // after this method returns, which is the same thing but less obvious at the call site.
        api.Dispose();
    }

    /// <summary>
    /// Merges this install's hook registrations into ~/.claude/settings.json. Idempotent, so running
    /// it on every launch also repairs a stale path after an upgrade.
    /// </summary>
    private static void EnsureHooksRegistered()
    {
        if (!HookPaths.HooksExecutableExists)
        {
            // Development build with no hook exe alongside; nothing useful to register.
            return;
        }

        try
        {
            ClaudeSettingsMerger.Merge(HookPaths.HooksExecutable);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Surfaced on demand via "Re-register hooks" rather than with a startup dialog:
            // the app launches at login and must never block the desktop with a modal error.
        }
    }
}
