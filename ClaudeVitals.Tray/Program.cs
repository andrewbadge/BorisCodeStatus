using ClaudeVitals.Api;
using ClaudeVitals.Core;
using ClaudeVitals.Core.State;
using Microsoft.AspNetCore.Builder;

namespace ClaudeVitals.Tray;

/// <summary>
/// Entry point for the user-mode relay. One process does everything: it hosts the ESP32-facing
/// HTTP endpoint in-process (no second executable to install or supervise) and owns the tray icon
/// on the same message loop.
/// </summary>
internal static class Program
{
    // Per-user, not global: the app is a per-user install and two different users signed into the
    // same machine legitimately run their own copies.
    private const string SingleInstanceMutexName = @"Local\ClaudeVitals.Tray.singleton";

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

        WebApplication? api = null;
        try
        {
            api = StartApi(options, store);
        }
        catch (Exception ex)
        {
            // Most likely the port is already taken. The tray is still worth running: the state
            // file and the icon work without the listener, and the message says what went wrong.
            MessageBox.Show(
                $"The status endpoint could not start on port {options.Port}.\r\n\r\n{ex.Message}\r\n\r\n" +
                $"Set the {VitalsApiOptions.PortEnvironmentVariable} environment variable to use a different port.",
                "Claude Code Vitals Relay",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        using var tray = new TrayIcon(store, options.Port);

        // Registering hooks is done here rather than in an MSI custom action — see README. It runs
        // after the tray is up so a locked or unusual settings.json never delays or fails startup.
        EnsureHooksRegistered();

        Application.Run();

        StopApi(api);
    }

    private static WebApplication StartApi(VitalsApiOptions options, VitalsStateStore store)
    {
        var app = VitalsApi.Build(options, store);

        // StartAsync rather than RunAsync: this thread must return to pump the WinForms message loop.
        app.StartAsync().GetAwaiter().GetResult();
        return app;
    }

    private static void StopApi(WebApplication? api)
    {
        if (api is null)
        {
            return;
        }

        try
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            api.StopAsync(shutdown.Token).GetAwaiter().GetResult();
            api.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Shutting down anyway.
        }
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
