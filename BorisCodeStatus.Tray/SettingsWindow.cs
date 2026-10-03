using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using BorisCodeStatus.Core;
using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Tray;

/// <summary>
/// The settings window — what right-clicking the tray icon opens, in place of the old context
/// menu. Drawn in the display card's language rather than as a stock dialog: the same dark bezel
/// colours, the pixel font for headings and the dog's palette for accents.
///
/// It owns no state. Every value is read from <see cref="TrayIcon"/>, and every switch calls the
/// same tray method the menu item used to, so the behaviour of each setting — persistence, the
/// HTTP start/stop off the UI thread, the balloon when a save fails — is unchanged. The tray calls
/// <see cref="RefreshFromTray"/> on every refresh, so the tiles and switches stay live.
///
/// Layout is in 96-DPI design pixels scaled to the monitor it opens on.
/// </summary>
// ponytail: scaled once, for the DPI it opens at. Dragged to a monitor with a different scale it
// keeps its original size; rebuild the controls in OnDpiChanged if that ever matters.
internal sealed class SettingsWindow : Form
{
    private const string RepositoryUrl = TrayIcon.RepositoryUrl;

    // Design geometry.
    private const int Width96 = 760;
    private const int Height96 = 612;
    private const int Margin96 = 24;
    private const int TilesTop = 46;
    private const int TileHeight = 112;
    private const int BodyTop = 172;
    private const int NavWidth = 204;
    private const int FooterTop = 540;
    private const int GroupWidth = 486;
    private const int RowHeight = 56;

    internal static readonly Color Back = Color.FromArgb(0x12, 0x11, 0x0D);
    internal static readonly Color Line = Color.FromArgb(0x2E, 0x2B, 0x24);
    internal static readonly Color Raised = Color.FromArgb(0x1E, 0x1C, 0x17);
    internal static readonly Color Ink = WaitingCard.TitleColor;
    internal static readonly Color Muted = WaitingCard.MutedColor;
    internal static readonly Color Accent = DogSprites.TrayPalette[5];

    private readonly TrayIcon _tray;
    private readonly float _scale;
    private readonly Font _body;
    private readonly Font _small;

    private readonly Panel[] _pages = new Panel[3];
    private readonly NavItem[] _nav = new NavItem[3];

    private readonly Toggle _pinned;
    private readonly Toggle _mini;
    private readonly Toggle _http;
    private readonly Toggle _usageApi;
    private readonly PixelButton _fix;
    private readonly Label _fixNote;
    private readonly Panel _apps;
    private readonly Choice _dismissOnly;
    private readonly MomentControls[] _moments;
    private readonly Dictionary<Pet, Choice> _pets = [];

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

    public SettingsWindow(TrayIcon tray)
    {
        _tray = tray;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Back;
        ShowInTaskbar = true;
        KeyPreview = true;
        Text = "BorisCodeStatus settings";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        // The handle first, so DeviceDpi is the monitor's rather than a guess.
        _ = Handle;
        _scale = DeviceDpi / 96f;
        _body = WaitingCard.CreateDetailFont(L(13));
        _small = WaitingCard.CreateDetailFont(L(11));
        ClientSize = new Size(L(Width96), L(Height96));

        SuspendLayout();

        var version = new PixelText($"BorisCodeStatus  v{TrayIcon.Version}", Muted, scale: 1, tracking: 2)
        {
            Location = P(Margin96, 20),
            Cursor = Cursors.Hand,
        };
        version.Click += (_, _) => _tray.OpenUrl(RepositoryUrl);
        Controls.Add(version);

        var close = new CloseButton { Bounds = R(Width96 - Margin96 - 16, 16, 16, 16) };
        close.Click += (_, _) => Close();
        Controls.Add(close);

        // Nav and pages.
        string[] names = ["Notify", "Card", "Advanced"];
        for (var i = 0; i < names.Length; i++)
        {
            var index = i;
            _nav[i] = new NavItem(names[i]) { Bounds = R(14, BodyTop + 16 + (i * 52), NavWidth - 28, 44) };
            _nav[i].Click += (_, _) => ShowPage(index);
            Controls.Add(_nav[i]);

            _pages[i] = new Panel
            {
                Bounds = R(NavWidth + 1, BodyTop + 1, Width96 - NavWidth - 4, FooterTop - BodyTop - 2),
                BackColor = Back,
                AutoScroll = true,
                Visible = false,
            };
            Controls.Add(_pages[i]);
        }

        // Notify.
        var notifyPage = _pages[0];
        Heading(notifyPage, "Notifications", 24);

        // One section per moment — Claude waiting on you, Claude finishing a turn — each with the
        // card, the sound, and which sound, all independent.
        _moments = [Moment(notifyPage, 64, NotifyMoment.Waiting, "When waiting"), Moment(notifyPage, 296, NotifyMoment.Idle, "When idle")];

        notifyPage.Controls.Add(new PixelText("When notification is clicked", Ink, scale: 1, tracking: 2, bold: true) { Location = P(Margin96, 528) });
        notifyPage.Controls.Add(new Label
        {
            Text = "Clicking a notification card closes it. It can also bring one of these apps to the front.",
            Font = _small,
            ForeColor = Muted,
            BackColor = Back,
            Bounds = R(Margin96, 546, GroupWidth, 36),
        });

        var dismissGroup = Group(notifyPage, 588, rows: 1, rowHeight: 48);
        _dismissOnly = new Choice("Just dismiss the notification", _body) { Bounds = R(16, 4, GroupWidth - 32, 40) };
        _dismissOnly.Click += (_, _) => SetClickTarget(null);
        dismissGroup.Controls.Add(_dismissOnly);

        _apps = Group(notifyPage, 648, rows: 1, rowHeight: 48);
        _apps.Tag = int.MaxValue; // One bordered box; the choices need no rules between them.

        // Card.
        var cardPage = _pages[1];
        Heading(cardPage, "Status card", 24);
        var cardGroup = Group(cardPage, 60, rows: 2);
        _pinned = new Toggle();
        _pinned.Click += (_, _) =>
        {
            _tray.ToggleStatusPinned();
            RefreshFromTray();
        };
        Row(cardGroup, 0, "Keep status card on screen", _pinned);
        _mini = new Toggle();
        _mini.Click += (_, _) =>
        {
            _tray.ToggleStatusMini();
            RefreshFromTray();
        };
        Row(cardGroup, 1, "Mini status card", _mini);

        cardPage.Controls.Add(new PixelText("Who keeps you company?", Ink, scale: 1, tracking: 2, bold: true) { Location = P(Margin96, 196) });
        var petGroup = Group(cardPage, 216, rows: 2);
        foreach (var pet in Enum.GetValues<Pet>())
        {
            var index = (int)pet;
            var label = pet switch { Pet.Bot => "Sentry bot", Pet.Duck => "Rubber duck", _ => pet.ToString() };
            var choice = new Choice(label, _body, DogSprites.Tray(DogState.Idle, pet))
            {
                Bounds = R(16 + (index % 2 * ((GroupWidth - 32) / 2)), 8 + (index / 2 * RowHeight), 200, 40),
            };
            choice.Click += (_, _) =>
            {
                _tray.SetPet(pet);
                RefreshFromTray();
            };
            _pets[pet] = choice;
            petGroup.Controls.Add(choice);
        }

        // Advanced.
        var advancedPage = _pages[2];
        Heading(advancedPage, "Advanced", 24);
        var serviceGroup = Group(advancedPage, 60, rows: 2);
        _http = new Toggle();
        _http.Click += (_, _) =>
        {
            _tray.ToggleHttp();
            RefreshFromTray();
        };
        Row(serviceGroup, 0, "Enable HTTP service", _http);
        _usageApi = new Toggle();
        _usageApi.Click += (_, _) =>
        {
            _tray.ToggleUsageApi();
            RefreshFromTray();
        };
        Row(serviceGroup, 1, "Use usage API for Sonnet quota", _usageApi);

        var actionGroup = Group(advancedPage, 188, rows: 3);
        var open = new PixelButton("Open");
        open.Click += (_, _) => _tray.OpenDashboard();
        Row(actionGroup, 0, "Open in browser", open);
        var reregister = new PixelButton("Re-register");
        reregister.Click += (_, _) => _tray.ReRegisterHooks();
        Row(actionGroup, 1, "Re-register hooks", reregister);
        _fix = new PixelButton("Fix...");
        _fix.Click += (_, _) => _tray.FixFirewallAccess();
        _fixNote = Row(actionGroup, 2, "Fix firewall access", _fix, note: "Turn on the HTTP service to use this.");

        // Footer. Exit is on the tray menu, not here.
        var done = new PixelButton("Close", filled: true) { Bounds = R(Width96 - Margin96 - 112, FooterTop + 16, 112, 40) };
        done.Click += (_, _) => Close();
        Controls.Add(done);

        ResumeLayout();

        ShowPage(0);
        RefreshFromTray();
    }

    private int L(float designPixels) => (int)Math.Round(designPixels * _scale);

    private Point P(int x, int y) => new(L(x), L(y));

    private Rectangle R(int x, int y, int width, int height) => new(L(x), L(y), L(width), L(height));

    /// <summary>Re-reads everything from the tray. Called by the tray on each refresh.</summary>
    public void RefreshFromTray()
    {
        _pinned.On = _tray.StatusPinned;
        _mini.On = _tray.StatusMini;
        _http.On = _tray.HttpOn;
        _http.Enabled = !_tray.HttpBusy;
        _usageApi.On = _tray.UsageApiOn;

        // Greyed out while the service is off: with nothing listening there is nothing for a
        // rule to let through, and adding one would open the firewall for no reason.
        _fix.Enabled = _tray.HttpOn;
        _fixNote.Visible = !_tray.HttpOn;

        var current = ClickTargetPreference.Get();
        foreach (var m in _moments)
        {
            m.Card.On = _tray.CardOn(m.Moment);
            m.Sound.On = _tray.SoundOn(m.Moment);
            var names = WaitingSound.Names(_tray.Pet);
            m.Default.Text = $"{names.Default} (default)";
            m.Alternate.Text = names.Alternate;
            m.Default.On = !_tray.SoundAlternate(m.Moment);
            m.Alternate.On = _tray.SoundAlternate(m.Moment);
        }

        foreach (var (pet, choice) in _pets)
        {
            choice.On = pet == _tray.Pet;
        }
        _dismissOnly.On = current is null;
        foreach (var choice in _apps.Controls.OfType<Choice>())
        {
            choice.On = string.Equals(choice.Tag as string, current, StringComparison.OrdinalIgnoreCase);
        }

        Invalidate(R(0, 0, Width96, BodyTop));
    }

    private void ShowPage(int index)
    {
        for (var i = 0; i < _pages.Length; i++)
        {
            _pages[i].Visible = i == index;
            _nav[i].Selected = i == index;
        }

        // Not before the window is up: OnShown fills it the first time, after the first paint.
        if (index == 0 && Visible)
        {
            FillApps();
        }
    }

    /// <summary>
    /// The apps with a window open now, two to a row, plus a saved choice that is not running —
    /// listed anyway, so the window never hides what is set. Rebuilt whenever the page is shown.
    /// </summary>
    private async void FillApps()
    {
        // One scan at a time: switching pages quickly must not stack them up.
        if (_filling)
        {
            return;
        }

        _filling = true;
        if (!_apps.Controls.OfType<Choice>().Any())
        {
            ReplaceApps([new Label
            {
                Text = "Looking for open apps...",
                Font = _small,
                ForeColor = Muted,
                BackColor = Back,
                AutoSize = true,
                Location = P(16, 16),
            }]);
            _apps.Height = L(48);
        }

        // Off the UI thread, so the window paints and stays responsive while it runs. An exception
        // here comes back through the await to the UI thread, where the top-level handler logs it.
        List<(string ProcessName, string Label)> apps;
        try
        {
            apps = [.. await Task.Run(WindowActivator.RunningApps)];
        }
        finally
        {
            _filling = false;
        }

        if (IsDisposed)
        {
            return;
        }

        var current = ClickTargetPreference.Get();
        if (current is not null && !apps.Any(a => a.ProcessName.Equals(current, StringComparison.OrdinalIgnoreCase)))
        {
            apps.Insert(0, (current, $"{current} (not running)"));
        }

        var choices = new List<Control>();
        var column = (GroupWidth - 32) / 2;
        for (var i = 0; i < apps.Count; i++)
        {
            var (processName, label) = apps[i];
            var choice = new Choice(label, _body)
            {
                Tag = processName,
                Bounds = R(16 + ((i % 2) * column), 8 + ((i / 2) * 44), column - 8, 40),
                On = processName.Equals(current, StringComparison.OrdinalIgnoreCase),
            };
            choice.Click += (_, _) => SetClickTarget(processName);
            choices.Add(choice);
        }

        ReplaceApps(choices);
        _apps.Height = L(16 + (Math.Max(1, (apps.Count + 1) / 2) * 44));
    }

    private bool _filling;

    private void ReplaceApps(IReadOnlyList<Control> replacements)
    {
        _apps.SuspendLayout();

        // Copied out first: disposing a control removes it from this collection, so disposing while
        // enumerating it would skip every other one.
        var old = _apps.Controls.Cast<Control>().ToArray();
        _apps.Controls.Clear();
        foreach (var control in old)
        {
            control.Dispose();
        }

        _apps.Controls.AddRange([.. replacements]);
        _apps.ResumeLayout();
    }

    private void SetClickTarget(string? processName)
    {
        if (!ClickTargetPreference.TrySet(processName))
        {
            _tray.ShowBalloon("That preference could not be saved.", ToolTipIcon.Warning);
        }

        RefreshFromTray();
    }

    /// <summary>The switches and sound choices for one <see cref="NotifyMoment"/>.</summary>
    private sealed record MomentControls(NotifyMoment Moment, Toggle Card, Toggle Sound, Choice Default, Choice Alternate);

    /// <summary>
    /// A section of the Notify page: a subheading, the card and sound switches, and the current
    /// pet's two sounds (relabelled by <see cref="RefreshFromTray"/> when the pet changes). 200
    /// design pixels tall from <paramref name="y"/>.
    /// </summary>
    private MomentControls Moment(Panel page, int y, NotifyMoment moment, string title)
    {
        page.Controls.Add(new PixelText(title, Ink, scale: 1, tracking: 2, bold: true) { Location = P(Margin96, y) });

        var switches = Group(page, y + 20, rows: 2);
        var card = new Toggle();
        card.Click += (_, _) =>
        {
            _tray.ToggleCard(moment);
            RefreshFromTray();
        };
        Row(switches, 0, "Show a card", card);

        var sound = new Toggle();
        sound.Click += (_, _) =>
        {
            _tray.ToggleSound(moment);
            RefreshFromTray();
        };
        Row(switches, 1, "Play a sound", sound);

        var sounds = Group(page, y + 144, rows: 1);
        var preferred = new Choice(string.Empty, _body) { Bounds = R(16, 8, 220, 40) };
        preferred.Click += (_, _) =>
        {
            _tray.SetSoundAlternate(moment, false);
            RefreshFromTray();
        };
        var alternate = new Choice(string.Empty, _body) { Bounds = R(16 + ((GroupWidth - 32) / 2), 8, 220, 40) };
        alternate.Click += (_, _) =>
        {
            _tray.SetSoundAlternate(moment, true);
            RefreshFromTray();
        };
        sounds.Controls.Add(preferred);
        sounds.Controls.Add(alternate);

        return new MomentControls(moment, card, sound, preferred, alternate);
    }

    private void Heading(Panel page, string text, int y) =>
        page.Controls.Add(new PixelText(text, Ink, scale: 2, tracking: 2, bold: true) { Location = P(Margin96, y) });

    /// <summary>A bordered group of rows, each separated by a rule.</summary>
    private Panel Group(Panel page, int y, int rows, int rowHeight = RowHeight)
    {
        var group = new Panel
        {
            Bounds = R(Margin96, y, GroupWidth, rows * rowHeight),
            BackColor = Back,
            Tag = L(rowHeight),
        };
        group.Paint += PaintGroup;
        page.Controls.Add(group);
        return group;
    }

    private void PaintGroup(object? sender, PaintEventArgs e)
    {
        var group = (Panel)sender!;
        var rowHeight = (int)group.Tag!;
        DrawBorder(e.Graphics, group.ClientRectangle);

        using var pen = new Pen(Line);
        for (var y = rowHeight; y < group.Height - 1; y += rowHeight)
        {
            e.Graphics.DrawLine(pen, 0, y, group.Width, y);
        }
    }

    /// <summary>One row: its label on the left, the control on the right, an optional note below the label.</summary>
    private Label Row(Panel group, int index, string text, Control control, string? note = null)
    {
        var top = L(index * RowHeight);
        var height = L(RowHeight);

        var label = new Label
        {
            Text = text,
            Font = _body,
            ForeColor = Ink,
            BackColor = Back,
            AutoSize = true,
            Location = new Point(L(18), top + (height / 2) - (note is null ? L(8) : L(15))),
        };
        group.Controls.Add(label);

        var noteLabel = new Label
        {
            Text = note ?? string.Empty,
            Font = _small,
            ForeColor = Muted,
            BackColor = Back,
            AutoSize = true,
            Location = new Point(L(18), top + (height / 2) + L(3)),
            Visible = note is not null,
        };
        group.Controls.Add(noteLabel);

        if (control is PixelButton)
        {
            control.Size = new Size(Math.Max(L(112), PixelButton.MeasureWidth(control.Text, this) + L(40)), L(40));
        }
        else
        {
            control.Size = new Size(L(92), L(26));
        }

        control.Location = new Point(L(GroupWidth - 18) - control.Width, top + ((height - control.Height) / 2));
        group.Controls.Add(control);
        return noteLabel;
    }

    /// <summary>Dark scrollbars on the page panels, to match; harmless where the theme is missing.</summary>
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        foreach (var page in _pages)
        {
            SetWindowTheme(page.Handle, "DarkMode_Explorer", null);
        }

        // Asynchronous: the window is already painted and usable while the list is gathered.
        FillApps();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Close();
        }
    }

    private const int WS_EX_COMPOSITED = 0x02000000;

    /// <summary>
    /// Composited: the window and all its child controls are painted together, off screen, and
    /// shown at once. Without it each of the dozens of small controls paints on its own, and the
    /// window first appears as grey blocks with whatever was behind it showing through.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_COMPOSITED;
            return parameters;
        }
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;

    /// <summary>Borderless, so the top strip above the tiles is the title bar to drag by.</summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST && m.Result == HTCLIENT)
        {
            var lParam = m.LParam.ToInt64();
            var point = PointToClient(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
            if (point.Y < L(TilesTop))
            {
                m.Result = HTCAPTION;
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;

        using (var pen = new Pen(WaitingCard.BorderColor, L(2)))
        {
            graphics.DrawRectangle(pen, L(1), L(1), ClientSize.Width - L(2), ClientSize.Height - L(2));
        }

        using (var pen = new Pen(Line))
        {
            graphics.DrawLine(pen, L(3), L(BodyTop), ClientSize.Width - L(3), L(BodyTop));
            graphics.DrawLine(pen, L(3), L(FooterTop), ClientSize.Width - L(3), L(FooterTop));
            graphics.DrawLine(pen, L(NavWidth), L(BodyTop), L(NavWidth), L(FooterTop));
        }

        DrawTiles(graphics);
    }

    /// <summary>The three tiles: status, the 5-hour session, the week.</summary>
    private void DrawTiles(Graphics graphics)
    {
        var state = _tray.State;
        var tileWidth = (Width96 - (2 * Margin96) - 24) / 3;
        var pixel = Math.Max(1, (int)Math.Round(_scale));

        for (var i = 0; i < 3; i++)
        {
            var tile = R(Margin96 + (i * (tileWidth + 12)), TilesTop, tileWidth, TileHeight);
            DrawBorder(graphics, tile);

            var x = tile.X + L(16);
            switch (i)
            {
                case 0:
                    var dog = DogStates.For(state, DateTimeOffset.UtcNow);
                    PixelFont.Draw(graphics, "Status", x, tile.Y + L(16), pixel, 2, Muted);
                    PixelFont.Draw(graphics, dog.ToString(), x, tile.Y + L(38), pixel * 2, 1, TrayIcon.AccentFor(dog), bold: true);
                    TextRenderer.DrawText(graphics, $"{(_tray.HttpOn ? "HTTP on" : "HTTP off")} · {(_tray.NotificationsOn ? "notify on" : "notify off")}", _small, new Point(x, tile.Y + L(70)), Muted, TextFormatFlags.NoPadding);
                    break;

                case 1:
                    DrawWindowTile(graphics, tile, x, "Session  5h", state.Session, pixel);
                    break;

                default:
                    DrawWindowTile(graphics, tile, x, "Week  7d", state.Week, pixel);
                    break;
            }
        }
    }

    private void DrawWindowTile(Graphics graphics, Rectangle tile, int x, string label, RateLimitWindow? window, int pixel)
    {
        PixelFont.Draw(graphics, label, x, tile.Y + L(16), pixel, 2, Muted);

        var used = window?.UsedPercentage;
        var value = used is { } u ? $"{u.ToString("0", CultureInfo.InvariantCulture)}% used" : "No data";
        PixelFont.Draw(graphics, value, x, tile.Y + L(38), pixel * 2, 1, Ink, bold: true);

        // Twenty segments, a twentieth each, in the ring's colour for this figure.
        const int segments = 20;
        var barWidth = tile.Width - L(32);
        var step = barWidth / segments;
        var lit = used is { } p ? (int)Math.Round(Math.Clamp(p, 0, 100) / 100 * segments) : 0;
        using var track = new SolidBrush(WaitingCard.BarTrackColor);
        using var fill = new SolidBrush(TrayIconRenderer.QuotaColorFor(used ?? 0));
        for (var s = 0; s < segments; s++)
        {
            graphics.FillRectangle(s < lit ? fill : track, x + (s * step), tile.Y + L(62), step - L(3), L(12));
        }

        if (window?.ResetsInMinutes is { } minutes)
        {
            TextRenderer.DrawText(graphics, $"Resets in {TrayIcon.FormatDuration(minutes)}", _small, new Point(x, tile.Y + L(84)), Muted, TextFormatFlags.NoPadding);
        }
    }

    private static void DrawBorder(Graphics graphics, Rectangle bounds)
    {
        using var pen = new Pen(Line);
        graphics.DrawRectangle(pen, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _body.Dispose();
            _small.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A double-buffered control that paints itself, with the window's pixel scale.</summary>
    private class Painted : Control
    {
        protected Painted()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Back;
            Cursor = Cursors.Hand;
        }

        protected int Pixel => Math.Max(1, (int)Math.Round(DeviceDpi / 96f));

        protected bool Hot { get; private set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            Hot = false;
            Invalidate();
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    /// <summary>One line of the pixel font, sized to fit.</summary>
    private sealed class PixelText : Painted
    {
        private readonly int _scale;
        private readonly int _tracking;
        private readonly bool _bold;

        public PixelText(string text, Color color, int scale, int tracking, bool bold = false)
        {
            Text = text;
            ForeColor = color;
            _scale = scale;
            _tracking = tracking;
            _bold = bold;
            Cursor = Cursors.Default;
            Size = new Size(PixelFont.Measure(text, Pixel * scale, tracking, bold) + 2, PixelFont.GlyphHeight * Pixel * scale);
        }

        protected override void OnPaint(PaintEventArgs e) =>
            PixelFont.Draw(e.Graphics, Text, 0, 0, Pixel * _scale, _tracking, Hot && Cursor == Cursors.Hand ? Ink : ForeColor, _bold);
    }

    /// <summary>The ON/OFF switch from the design: a label, then a box with a square knob.</summary>
    private sealed class Toggle : Painted
    {
        private bool _on;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool On
        {
            get => _on;
            set
            {
                if (_on != value)
                {
                    _on = value;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            var p = Pixel;
            var box = new Rectangle(Width - (p * 46), (Height - (p * 22)) / 2, p * 46, p * 22);
            var dim = Enabled ? 1f : 0.4f;

            var label = _on ? "On" : "Off";
            var labelWidth = PixelFont.Measure(label, p, 2);
            PixelFont.Draw(graphics, label, box.X - (p * 12) - labelWidth, (Height - (PixelFont.GlyphHeight * p)) / 2, p, 2, Fade(Muted, dim));

            if (_on)
            {
                using var fill = new SolidBrush(Fade(Hot ? Brighten(Accent) : Accent, dim));
                graphics.FillRectangle(fill, box);
                using var knob = new SolidBrush(Back);
                graphics.FillRectangle(knob, box.Right - (p * 18), box.Y + (p * 4), p * 14, p * 14);
            }
            else
            {
                using var pen = new Pen(Fade(Hot ? Ink : Muted, dim), p * 2);
                graphics.DrawRectangle(pen, box.X + p, box.Y + p, box.Width - (p * 2), box.Height - (p * 2));
                using var knob = new SolidBrush(Fade(Muted, dim));
                graphics.FillRectangle(knob, box.X + (p * 4), box.Y + (p * 4), p * 14, p * 14);
            }
        }
    }

    /// <summary>An outlined pixel-font button, or the filled accent one for the primary action.</summary>
    private sealed class PixelButton : Painted
    {
        private readonly bool _filled;

        public PixelButton(string text, bool filled = false)
        {
            Text = text;
            _filled = filled;
        }

        public static int MeasureWidth(string text, Control scaleFrom) =>
            PixelFont.Measure(text, Math.Max(1, (int)Math.Round(scaleFrom.DeviceDpi / 96f)), 2, bold: true);

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            var p = Pixel;
            Color ink;

            if (_filled)
            {
                using var fill = new SolidBrush(Hot ? Brighten(Accent) : Accent);
                graphics.FillRectangle(fill, ClientRectangle);
                ink = Back;
            }
            else
            {
                ink = Enabled ? Ink : Line;
                using var pen = new Pen(Enabled ? (Hot ? Ink : Muted) : Line, p * 2);
                graphics.DrawRectangle(pen, p, p, Width - (p * 2), Height - (p * 2));
                if (Hot && Enabled)
                {
                    using var fill = new SolidBrush(Raised);
                    graphics.FillRectangle(fill, p * 2, p * 2, Width - (p * 4), Height - (p * 4));
                }
            }

            var width = PixelFont.Measure(Text, p, 2, bold: true);
            PixelFont.Draw(graphics, Text, (Width - width) / 2, (Height - (PixelFont.GlyphHeight * p)) / 2, p, 2, ink, bold: true);
        }
    }

    /// <summary>A square choice box with its label. One of a group is on; the window keeps them exclusive.</summary>
    private sealed class Choice : Painted
    {
        private readonly Font _font;
        private readonly (string[] Grid, Color[] Palette)? _icon;
        private bool _on;

        /// <summary>Optionally with a 16px sprite beside the box, drawn at two pixels per cell.</summary>
        public Choice(string text, Font font, (string[] Grid, Color[] Palette)? icon = null)
        {
            Text = text;
            _font = font;
            _icon = icon;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool On
        {
            get => _on;
            set
            {
                if (_on != value)
                {
                    _on = value;
                    Invalidate();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            var p = Pixel;
            var size = p * 18;
            var box = new Rectangle(0, (Height - size) / 2, size, size);

            using (var pen = new Pen(_on ? Accent : Hot ? Ink : Muted, p * 2))
            {
                graphics.DrawRectangle(pen, box.X + p, box.Y + p, box.Width - (p * 2), box.Height - (p * 2));
            }

            if (_on)
            {
                using var fill = new SolidBrush(Accent);
                graphics.FillRectangle(fill, box.X + (p * 5), box.Y + (p * 5), box.Width - (p * 10), box.Height - (p * 10));
            }

            var textLeft = size + (p * 14);
            if (_icon is { } icon)
            {
                var cell = p * 2;
                var top = (Height - (icon.Grid.Length * cell)) / 2;
                for (var row = 0; row < icon.Grid.Length; row++)
                {
                    for (var column = 0; column < icon.Grid[row].Length; column++)
                    {
                        var index = DogSprites.IndexOf(icon.Grid[row][column]);
                        if (index != 0)
                        {
                            using var brush = new SolidBrush(icon.Palette[index]);
                            graphics.FillRectangle(brush, textLeft + (column * cell), top + (row * cell), cell, cell);
                        }
                    }
                }

                textLeft += (icon.Grid[0].Length * cell) + (p * 12);
            }

            var textBounds = new Rectangle(textLeft, 0, Width - textLeft, Height);
            TextRenderer.DrawText(graphics, Text, _font, textBounds, Hot || _on ? Ink : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>A nav entry; the selected one is raised, boxed and marked with an accent chevron.</summary>
    private sealed class NavItem : Painted
    {
        private bool _selected;

        public NavItem(string text) => Text = text;

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var graphics = e.Graphics;
            var p = Pixel;

            if (_selected)
            {
                using var fill = new SolidBrush(Raised);
                graphics.FillRectangle(fill, ClientRectangle);
                using var pen = new Pen(Line);
                graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                PixelFont.Draw(graphics, ">", p * 16, (Height - (PixelFont.GlyphHeight * p)) / 2, p, 2, Accent);
            }

            var color = _selected ? Accent : Hot ? Brighten(Ink) : Ink;
            PixelFont.Draw(graphics, Text, p * 38, (Height - (PixelFont.GlyphHeight * p)) / 2, p, 3, color, bold: true);
        }
    }

    /// <summary>The faint × in the corner, brightening under the pointer like the card's.</summary>
    private sealed class CloseButton : Painted
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var p = Pixel;
            using var pen = new Pen(Hot ? Ink : Muted, p * 2);
            var inset = p * 3;
            e.Graphics.DrawLine(pen, inset, inset, Width - inset, Height - inset);
            e.Graphics.DrawLine(pen, Width - inset, inset, inset, Height - inset);
        }
    }

    private static Color Fade(Color color, float amount) =>
        amount >= 1 ? color : Color.FromArgb((int)(255 * amount), color);

    private static Color Brighten(Color color) => Color.FromArgb(
        Math.Min(255, color.R + 30),
        Math.Min(255, color.G + 30),
        Math.Min(255, color.B + 30));
}
