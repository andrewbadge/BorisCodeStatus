using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Net.Sockets;
using System.Reflection;
using BorisClaudeNotifications.Api;
using BorisClaudeNotifications.Core;
using BorisClaudeNotifications.Core.Models;
using BorisClaudeNotifications.Core.State;

namespace BorisClaudeNotifications.Tray;

/// <summary>
/// The whole visible UI: a tray icon with a context menu. There is no window and no console —
/// the app is a background relay that happens to have a status glyph.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    /// <summary>Where the menu header points. Private today, so this may 404 for anyone else.</summary>
    private const string RepositoryUrl = "https://github.com/andrewbadge/BorisClaudeNotifications";

    private readonly NotifyIcon _notifyIcon;
    private readonly VitalsStateStore _store;
    private readonly VitalsApiHost _api;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _notificationsItem;
    private readonly int _port;
    private readonly SynchronizationContext _uiContext;

    private readonly ToolStripMenuItem _sessionItem;
    private readonly ToolStripMenuItem _weekItem;
    private readonly ToolStripMenuItem _activityItem;

    /// <summary>
    /// Drives the dog to sleep after a quiet spell. Nothing writes to the state file while a
    /// session is idle, so without a clock of its own the icon would sit awake indefinitely.
    /// Half a minute is well under <see cref="DogStates.SleepAfter"/> and costs nothing: the
    /// redraw is skipped unless the pose or the quota bucket actually changed.
    /// </summary>
    private readonly System.Windows.Forms.Timer _poseTimer;

    private Icon? _currentIcon;
    private DogState _renderedDog = (DogState)(-1);
    private int _renderedQuotaBucket = -1;
    private bool _pausePersisted = true;

    /// <summary>
    /// When the wait we last notified about began. Guards against re-notifying the same prompt
    /// every time <see cref="Refresh"/> runs, which includes every pose-timer tick.
    /// </summary>
    private DateTimeOffset? _lastNotifiedWaitAt;

    /// <summary>The waiting notification. Null until the first one is shown.</summary>
    private WaitingCard? _waitingCard;

    private bool _disposed;

    public TrayIcon(VitalsStateStore store, int port, VitalsApiHost api)
    {
        _store = store;
        _port = port;
        _api = api;

        // Captured on the UI thread so state changes (raised on thread-pool threads by the
        // file watcher) can be marshalled back before touching NotifyIcon.
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _sessionItem = new ToolStripMenuItem("Session: —") { Enabled = false };
        _weekItem = new ToolStripMenuItem("Week: —") { Enabled = false };
        _activityItem = new ToolStripMenuItem("Status: —") { Enabled = false };

        // Checked-state toggle rather than a label that flips between Pause and Resume: the tick
        // says what is true now, where a verb only says what the click will do.
        _pauseItem = new ToolStripMenuItem("Pause HTTP service", null, (_, _) => TogglePaused())
        {
            CheckOnClick = false,
        };

        // Everything actionable sits under Advanced: the top level is then purely the current
        // figures, which is what someone opening the menu is almost always here to read.
        // Double-clicking the icon still opens the browser, so the common action keeps a shortcut.
        // Enabled by default; the tick reflects what is stored, not what a click will do.
        _notificationsItem = new ToolStripMenuItem("Notify when waiting", null, (_, _) => ToggleNotifications())
        {
            Checked = NotificationPreference.AreEnabled(),
            CheckOnClick = false,
        };

        var advanced = new ToolStripMenuItem("Advanced");
        advanced.DropDownItems.Add(_notificationsItem);
        advanced.DropDownItems.Add(_pauseItem);
        advanced.DropDownItems.Add(new ToolStripSeparator());
        advanced.DropDownItems.Add(new ToolStripMenuItem("Open in Browser", null, (_, _) => OpenDashboard()));
        advanced.DropDownItems.Add(new ToolStripMenuItem("Re-register hooks", null, (_, _) => ReRegisterHooks()));
        advanced.DropDownItems.Add(new ToolStripMenuItem("Fix firewall access...", null, (_, _) => FixFirewallAccess()));

        // Header: what is running, and a way to get to the source. Left enabled so it can be
        // clicked; the repo is private today, so for anyone but the owner this will land on
        // GitHub's 404 rather than the project — acceptable, and self-correcting if it opens up.
        var titleItem = new ToolStripMenuItem($"BorisClaudeNotifications v{BuildVersion}", null, (_, _) => OpenUrl(RepositoryUrl))
        {
            Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(titleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_activityItem);
        menu.Items.Add(_sessionItem);
        menu.Items.Add(_weekItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(advanced);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Application.Exit()));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = true,
            Text = "BorisClaudeNotifications",
        };
        _notifyIcon.DoubleClick += (_, _) => OpenDashboard();

        Refresh(_store.Current);

        _poseTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _poseTimer.Tick += (_, _) => Refresh(_store.Current);
        _poseTimer.Start();

        _store.Changed += OnStateChanged;
        _store.StartWatching();

        WarnIfFirewallBlocksTheDisplay();
    }

    /// <summary>
    /// Checks once at startup whether the firewall will stop the ESP32 reaching us, and says so.
    /// Without this the failure is silent and looks like the app is broken: /status answers
    /// perfectly from this PC while the display never updates.
    ///
    /// Enumerating firewall rules can take a moment, so it runs off the UI thread.
    /// </summary>
    private void WarnIfFirewallBlocksTheDisplay()
    {
        Task.Run(() =>
        {
            var verdict = FirewallGuard.Detect(_port);
            if (!verdict.BlocksTheDisplay)
            {
                return;
            }

            _uiContext.Post(
                _ =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    var reason = verdict.State == FirewallState.Blocked
                        ? "the Windows firewall prompt was dismissed, which blocks it"
                        : "no firewall rule allows it";

                    ShowBalloon(
                        $"Your display probably cannot reach this PC, because {reason}. " +
                        "Right-click here and choose \"Fix firewall access\".",
                        ToolTipIcon.Warning);
                },
                null);
        });
    }

    private void OnStateChanged(VitalsState state) => _uiContext.Post(_ => Refresh(state), null);

    private void Refresh(VitalsState state)
    {
        if (_disposed)
        {
            return;
        }

        var paused = !_api.IsListening;

        // A pause is invisible from the outside — the tray icon looks identical and the display
        // just goes dark — so say so everywhere the user might look.
        _activityItem.Text = paused ? "Status: HTTP paused" : $"Status: {Describe(state)}";
        _sessionItem.Text = $"Session (5h): {FormatWindow(state.Session)}";
        _weekItem.Text = $"Week (7d): {FormatWindow(state.Week)}";
        _notifyIcon.Text = paused ? "BorisClaudeNotifications — HTTP paused" : BuildTooltip(state);
        _pauseItem.Checked = paused;

        UpdateIcon(state);
        NotifyIfWaitingForInput(state);
    }

    /// <summary>
    /// Raises a notification when Claude Code starts waiting on the user — the one state the user
    /// needs to act on, and the easiest to miss while looking at something else.
    ///
    /// Fires on the transition only. <see cref="Refresh"/> runs on every state write and every
    /// pose-timer tick, so notifying on "is Waiting" would repeat the same prompt indefinitely;
    /// keying off <see cref="VitalsState.ActivityChangedUtc"/> means one notification per wait,
    /// and a second prompt in the same session still gets its own because the timestamp moves.
    /// </summary>
    private void NotifyIfWaitingForInput(VitalsState state)
    {
        if (state.Activity != ActivityState.Waiting)
        {
            // Forget the last one, so returning to Waiting later notifies again even in the
            // unlikely event the timestamp repeats.
            _lastNotifiedWaitAt = null;

            // The prompt has been answered in the terminal, so the card is now wrong — take it
            // down rather than leave it counting out its lifetime. A balloon could not be recalled;
            // this is one of the things the card buys.
            _waitingCard?.Dismiss();
            return;
        }

        var waitingSince = state.ActivityChangedUtc;
        if (waitingSince == _lastNotifiedWaitAt)
        {
            return;
        }

        _lastNotifiedWaitAt = waitingSince;

        // Checked at the moment of use rather than cached, so turning notifications off takes
        // effect immediately rather than at the next restart.
        if (!NotificationPreference.AreEnabled())
        {
            return;
        }

        // The hook's own text names what is blocked, e.g. "Claude needs your permission to use
        // Bash"; WaitingPrompt falls back to something plain if it is absent.
        ShowWaitingCard(WaitingPrompt.From(state.WaitingMessage));
    }

    /// <summary>
    /// Created on first use rather than at startup: someone who never meets a prompt, or has
    /// notifications off, never pays for the window.
    /// </summary>
    private void ShowWaitingCard(WaitingPrompt prompt)
    {
        _waitingCard ??= new WaitingCard();
        _waitingCard.ShowPrompt(prompt);
    }

    private void UpdateIcon(VitalsState state)
    {
        // Redraw only on a visible change: the arc moves in 5% steps, so this is a handful of
        // renders per session rather than one per hook event.
        var bucket = state.Session?.UsedPercentage is { } used ? (int)(Math.Clamp(used, 0, 100) / 5) : -1;
        var dog = DogStates.For(state, DateTimeOffset.UtcNow);
        if (dog == _renderedDog && bucket == _renderedQuotaBucket && _currentIcon is not null)
        {
            return;
        }

        var replacement = TrayIconRenderer.Render(dog, state.Session?.UsedPercentage);
        var previous = _currentIcon;

        _notifyIcon.Icon = replacement;
        _currentIcon = replacement;
        _renderedDog = dog;
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
        var lines = new List<string> { $"BorisClaudeNotifications — {Describe(state)}" };

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

    /// <summary>
    /// Stops or restarts the listener, leaving the tray and the hooks running. Paused means the
    /// port is released, so the display gets a refused connection — the case its "relay down"
    /// handling already covers — rather than an error response it would need to understand.
    ///
    /// Deliberately not persisted: a pause lasts until the app restarts. Persisting it would let
    /// someone pause, forget, reboot weeks later and be left debugging a display that was switched
    /// off on purpose.
    /// </summary>
    private void TogglePaused()
    {
        var pausing = _api.IsListening;

        // Off the UI thread, and not merely to keep the menu painting: stopping Kestrel takes
        // long enough to be felt, and the tray must stay responsive throughout. The item is
        // disabled meanwhile so a second click cannot start a competing transition.
        _pauseItem.Enabled = false;

        Task.Run(() =>
        {
            try
            {
                if (pausing)
                {
                    _api.Stop();
                }
                else
                {
                    _api.Start();
                }

                // Only recorded once the change has actually taken effect, so a failed resume
                // cannot leave the preference claiming the service is running.
                _pausePersisted = PausePreference.TrySet(pausing);
                return null as string;
            }
            catch (Exception ex) when (ex is IOException or SocketException or InvalidOperationException)
            {
                // Most likely something else grabbed the port while we were paused.
                return ex.Message;
            }
        })
        .ContinueWith(
            task => _uiContext.Post(
                _ =>
                {
                    if (_disposed)
                    {
                        return;
                    }

                    _pauseItem.Enabled = true;

                    var failure = task.IsFaulted ? task.Exception?.GetBaseException().Message : task.Result;
                    if (failure is not null)
                    {
                        ShowBalloon(
                            $"Could not {(pausing ? "pause" : "resume")} the HTTP service: {failure}",
                            ToolTipIcon.Warning);
                    }
                    else if (pausing)
                    {
                        // Say when the pause will not survive a restart, rather than let someone
                        // discover it by finding the endpoint back up after a reboot.
                        var persistence = _pausePersisted
                            ? " It will stay paused until you resume, including after a restart."
                            : " Note: the setting could not be saved, so it will resume on restart.";

                        ShowBalloon(
                            $"HTTP service paused. Port {_port} is closed, so your display will show " +
                            "the relay as down." + persistence,
                            ToolTipIcon.Info);
                    }
                    else
                    {
                        ShowBalloon($"HTTP service resumed on port {_port}.", ToolTipIcon.Info);
                    }

                    Refresh(_store.Current);
                },
                null),
            TaskScheduler.Default);
    }

    /// <summary>
    /// Turns the waiting notification on or off. Cheap enough to do inline on the UI thread —
    /// it is one small file write, unlike pausing, which stops a web host.
    /// </summary>
    private void ToggleNotifications()
    {
        var enabled = !_notificationsItem.Checked;
        _notificationsItem.Checked = enabled;

        if (!NotificationPreference.TrySetEnabled(enabled))
        {
            // The tick already moved, and the setting is read at the moment of use, so the change
            // is live either way — it just will not survive a restart.
            ShowBalloon(
                "That preference could not be saved, so notifications will return to their "
                + "previous setting when BorisClaudeNotifications restarts.",
                ToolTipIcon.Warning);
            return;
        }

        // Confirm only when switching on, and by demonstrating the thing itself. Confirming a
        // switch-off with a notification would be a small joke at the user's expense.
        if (enabled)
        {
            ShowWaitingCard(new WaitingPrompt(
                "Notifications on",
                "You will see this card whenever Claude is waiting for you.",
                Hint: null));
        }
    }

    private void OpenDashboard() => OpenUrl($"http://localhost:{_port}/status");

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowBalloon("Could not open the browser.", ToolTipIcon.Warning);
        }
    }

    /// <summary>
    /// The version stamped at build time, for the menu header. Informational version is the one
    /// that carries the value from Directory.Build.props; anything after a '+' is build metadata
    /// the user has no use for.
    /// </summary>
    private static string BuildVersion
    {
        get
        {
            var informational = typeof(TrayIcon).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (string.IsNullOrWhiteSpace(informational))
            {
                return typeof(TrayIcon).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            }

            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus < 0 ? informational : informational[..plus];
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

    /// <summary>
    /// Reports the current firewall state and offers the two things a user can actually do:
    /// run the fix elevated (if an administrator is available), or copy the command to send to
    /// whoever administers the machine. Re-runnable at any time — the fix is idempotent.
    /// </summary>
    private void FixFirewallAccess()
    {
        var verdict = FirewallGuard.Detect(_port);

        if (verdict.State == FirewallState.Allowed)
        {
            var again = MessageBox.Show(
                $"A firewall rule already allows the relay on your {verdict.ProfileName} network, " +
                "so your display should be able to reach it.\r\n\r\nRe-apply the rule anyway?",
                "BorisClaudeNotifications — firewall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (again != DialogResult.Yes)
            {
                return;
            }
        }

        // Only open the endpoint on a "Public" network if the user knowingly asks for it: /status
        // has no authentication, so this decision is theirs to make explicitly.
        var includePublic = false;
        if (verdict.IsPublicNetwork)
        {
            var choice = MessageBox.Show(
                "Windows currently classes this network as Public.\r\n\r\n" +
                "The status endpoint has no authentication, so allowing it on a public network " +
                "would expose your usage and cost data to anyone on that network.\r\n\r\n" +
                "If this is really your home or office network, the better fix is to have an " +
                "administrator mark it Private. The command copied by this dialog includes that step.\r\n\r\n" +
                "Allow on the Public profile anyway?",
                "BorisClaudeNotifications — public network",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Warning);

            if (choice == DialogResult.Cancel)
            {
                return;
            }

            includePublic = choice == DialogResult.Yes;
        }

        var action = MessageBox.Show(
            DescribeFirewallState(verdict) + "\r\n\r\n" +
            "Changing firewall rules needs administrator rights.\r\n\r\n" +
            "  Yes  — try now (Windows will ask for administrator approval)\r\n" +
            "  No   — copy the command, to send to whoever administers this PC\r\n" +
            "  Cancel — do nothing",
            "BorisClaudeNotifications — fix firewall access",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        switch (action)
        {
            case DialogResult.Yes:
                var applied = FirewallGuard.TryApplyElevated(includePublic, out var message);
                ShowBalloon(message, applied ? ToolTipIcon.Info : ToolTipIcon.Warning);
                break;

            case DialogResult.No:
                var copied = FirewallGuard.TryCopyScriptToClipboard(includePublic);
                ShowBalloon(
                    copied
                        ? "Command copied. Send it to your administrator and ask them to run it in PowerShell."
                        : "Could not copy to the clipboard.",
                    copied ? ToolTipIcon.Info : ToolTipIcon.Warning);
                break;
        }
    }

    private static string DescribeFirewallState(FirewallVerdict verdict) => verdict.State switch
    {
        FirewallState.Blocked =>
            $"The firewall is blocking the relay on your {verdict.ProfileName} network.\r\n\r\n" +
            "This happens when the Windows firewall prompt is dismissed: Windows does not skip " +
            "the rule, it records one that blocks the app. Your display cannot reach this PC.",

        FirewallState.NoRule =>
            $"No firewall rule allows the relay on your {verdict.ProfileName} network, so Windows " +
            "will refuse incoming connections. Your display cannot reach this PC.",

        FirewallState.Allowed =>
            $"A rule already allows the relay on your {verdict.ProfileName} network.",

        _ =>
            "The firewall state could not be determined. Applying the rule is harmless either way.",
    };

    private void ShowBalloon(string message, ToolTipIcon icon, string? title = null)
    {
        _notifyIcon.BalloonTipTitle = title ?? "BorisClaudeNotifications";
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
        _poseTimer.Stop();
        _poseTimer.Dispose();
        _waitingCard?.Dispose();
        _waitingCard = null;

        // Hide before disposing, or the icon lingers in the tray until the user hovers over it.
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        TrayIconRenderer.Release(_currentIcon);
        _currentIcon = null;
    }
}
