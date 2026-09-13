using System.Diagnostics;
using System.Globalization;
using ClaudeVitals.Core;
using ClaudeVitals.Core.Models;
using ClaudeVitals.Core.State;

namespace ClaudeVitals.Tray;

/// <summary>
/// The whole visible UI: a tray icon with a context menu. There is no window and no console —
/// the app is a background relay that happens to have a status glyph.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly VitalsStateStore _store;
    private readonly int _port;
    private readonly SynchronizationContext _uiContext;

    private readonly ToolStripMenuItem _sessionItem;
    private readonly ToolStripMenuItem _weekItem;
    private readonly ToolStripMenuItem _activityItem;

    private Icon? _currentIcon;
    private ActivityState _renderedActivity = (ActivityState)(-1);
    private int _renderedQuotaBucket = -1;
    private bool _disposed;

    public TrayIcon(VitalsStateStore store, int port)
    {
        _store = store;
        _port = port;

        // Captured on the UI thread so state changes (raised on thread-pool threads by the
        // file watcher) can be marshalled back before touching NotifyIcon.
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _sessionItem = new ToolStripMenuItem("Session: —") { Enabled = false };
        _weekItem = new ToolStripMenuItem("Week: —") { Enabled = false };
        _activityItem = new ToolStripMenuItem("Status: —") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_activityItem);
        menu.Items.Add(_sessionItem);
        menu.Items.Add(_weekItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Open dashboard", null, (_, _) => OpenDashboard()));
        menu.Items.Add(new ToolStripMenuItem("Re-register hooks", null, (_, _) => ReRegisterHooks()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Exit()));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "Claude Code Vitals Relay",
        };
        _notifyIcon.DoubleClick += (_, _) => OpenDashboard();

        Refresh(_store.Current);

        _store.Changed += OnStateChanged;
        _store.StartWatching();
    }

    private void OnStateChanged(VitalsState state) => _uiContext.Post(_ => Refresh(state), null);

    private void Refresh(VitalsState state)
    {
        if (_disposed)
        {
            return;
        }

        _activityItem.Text = $"Status: {Describe(state)}";
        _sessionItem.Text = $"Session (5h): {FormatWindow(state.Session)}";
        _weekItem.Text = $"Week (7d): {FormatWindow(state.Week)}";
        _notifyIcon.Text = BuildTooltip(state);

        UpdateIcon(state);
    }

    private void UpdateIcon(VitalsState state)
    {
        // Redraw only on a visible change: the arc moves in 5% steps, so this is a handful of
        // renders per session rather than one per hook event.
        var bucket = state.Session?.UsedPercentage is { } used ? (int)(Math.Clamp(used, 0, 100) / 5) : -1;
        if (state.Activity == _renderedActivity && bucket == _renderedQuotaBucket && _currentIcon is not null)
        {
            return;
        }

        var replacement = TrayIconRenderer.Render(state.Activity, state.Session?.UsedPercentage);
        var previous = _currentIcon;

        _notifyIcon.Icon = replacement;
        _currentIcon = replacement;
        _renderedActivity = state.Activity;
        _renderedQuotaBucket = bucket;

        // Only release the old icon after the tray has taken the new one.
        TrayIconRenderer.Release(previous);
    }

    private static string Describe(VitalsState state) => state.Activity switch
    {
        ActivityState.Working => "Working",
        ActivityState.Waiting => "Waiting for you",
        ActivityState.Idle => "Idle",
        _ => "No session seen yet",
    };

    private static string FormatWindow(RateLimitWindow? window)
    {
        if (window?.UsedPercentage is not { } used)
        {
            return "—";
        }

        var text = $"{used.ToString("0.#", CultureInfo.CurrentCulture)}% used";
        return window.ResetsInMinutes is { } minutes
            ? $"{text}, resets in {FormatDuration(minutes)}"
            : text;
    }

    private static string FormatDuration(int minutes) => minutes switch
    {
        < 60 => $"{minutes}m",
        < 60 * 24 => $"{minutes / 60}h {minutes % 60}m",
        _ => $"{minutes / (60 * 24)}d {minutes % (60 * 24) / 60}h",
    };

    private string BuildTooltip(VitalsState state)
    {
        var lines = new List<string> { $"Claude Vitals — {Describe(state)}" };

        if (state.Session?.UsedPercentage is { } session)
        {
            lines.Add($"5h {session.ToString("0", CultureInfo.CurrentCulture)}%");
        }

        if (state.Week?.UsedPercentage is { } week)
        {
            lines.Add($"7d {week.ToString("0", CultureInfo.CurrentCulture)}%");
        }

        // The tray tooltip is hard-capped at 63 characters; longer text is silently dropped.
        var text = string.Join("  ", lines);
        return text.Length <= 63 ? text : text[..63];
    }

    private void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo($"http://localhost:{_port}/status") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowBalloon("Could not open the browser.", ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// Re-runs the settings.json merge. Useful after an upgrade moves the install path, or if the
    /// user edited settings.json by hand and dropped the hook entries.
    /// </summary>
    private void ReRegisterHooks()
    {
        try
        {
            var result = ClaudeSettingsMerger.Merge(HookPaths.HooksExecutable);

            var message = result.Warnings.Count > 0
                ? string.Join(" ", result.Warnings)
                : result.Changed
                    ? "Hooks registered in settings.json. Restart Claude Code to pick them up."
                    : "Hooks were already registered correctly.";

            ShowBalloon(message, result.Warnings.Count > 0 ? ToolTipIcon.Warning : ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowBalloon($"Could not update settings.json: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void ShowBalloon(string message, ToolTipIcon icon)
    {
        _notifyIcon.BalloonTipTitle = "Claude Code Vitals Relay";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = icon;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnStateChanged;

        // Hide before disposing, or the icon lingers in the tray until the user hovers over it.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        TrayIconRenderer.Release(_currentIcon);
        _currentIcon = null;
    }
}
