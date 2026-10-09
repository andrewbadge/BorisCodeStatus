using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using BorisCodeStatus.Core.Models;
using BorisCodeStatus.Core.State;

namespace BorisCodeStatus.Tray;

/// <summary>
/// The "Claude is waiting for you" notification, drawn as the design's display card rather than a
/// Windows balloon: a bezel around a 320×240 screen — the ESP32 panel's own size — with the
/// waiting dog, a pixel-font heading and a bar. A balloon's layout belongs to Windows and cannot
/// carry any of that, which is the whole reason this is a window of its own.
///
/// It never takes focus. It appears while the user is typing somewhere else, and stealing the
/// keyboard would send their next keystrokes into a window that cannot use them — or, worse, keep
/// them from the terminal where the prompt actually has to be answered. So it is a non-activating
/// tool window: no taskbar button, no focus on show or on click. A click dismisses it.
///
/// The bar counts down to the card hiding itself, and holds while the pointer is over the card.
///
/// A second instance is the status card that double-clicking the tray icon shows: the tray dog in
/// its current pose, the quota figures, and a bar that is the session gauge rather than a
/// countdown. <see cref="CardContent"/> carries the difference.
///
/// All layout is in the design's 96-DPI pixels and scaled to the monitor. The sprite and the pixel
/// font are the exception: they snap to a whole number of device pixels per design pixel, because
/// pixel art only survives integer scaling.
/// </summary>
internal sealed class WaitingCard : Form
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(12);

    /// <summary>The design's bang blinks at 2 fps: half a second on, half off.</summary>
    private static readonly TimeSpan BangBlink = TimeSpan.FromMilliseconds(500);

    // Design geometry, in 96-DPI pixels. The screen is 320×240 inside a 3px border and a 14px
    // frame, with a deeper chin below, as sampled from the design's card.
    private const int Border = 3;
    private const int Frame = 14;
    private const int Chin = 22;
    private const int ScreenWidth = 320;
    private const int ScreenHeight = 240;
    private const int Inset = 16;
    private const int DogX = 40;
    private const int DogY = 77;
    private const int SpritePixel = 4;
    private const int TextX = 158;
    private const int HeaderY = 17;
    private const int TitleY = 102;
    private const int TitleLineHeight = 17;
    private const int BarY = 218;
    private const int BarWidth = 249;
    private const int BarHeight = 6;
    private const int ScreenMarginFromTaskbar = 12;

    // The mini status card: two thirds of the full card's width, a fifth of its height, with a
    // thinner frame so the screen keeps most of what little room there is.
    private const int MiniWidth = 236;
    private const int MiniHeight = 56;
    private const int MiniFrame = 4;

    internal static readonly Color BorderColor = Color.FromArgb(0x3A, 0x36, 0x2B);
    internal static readonly Color FrameColor = Color.FromArgb(0x0C, 0x0B, 0x08);
    internal static readonly Color ScreenColor = Color.FromArgb(0x14, 0x12, 0x0C);
    internal static readonly Color MutedColor = Color.FromArgb(0x9A, 0x93, 0x84);
    internal static readonly Color TitleColor = DogSprites.Palette[2];
    internal static readonly Color BarTrackColor = Color.FromArgb(0x2A, 0x27, 0x1F);

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 50 };
    private Font? _detailFont;
    private CardContent _content = CardContent.From(WaitingPrompt.From(null));
    private TimeSpan _remaining;
    private DateTime _lastTick;
    private DateTime _shownAt;

    public WaitingCard()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = FrameColor;
        Cursor = Cursors.Hand;
        Text = "BorisCodeStatus — Claude is waiting";

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _ticker.Tick += (_, _) => Tick();
        Click += (_, _) => Dismiss();
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>
    /// Topmost via the extended style rather than the <see cref="Form.TopMost"/> property: the
    /// property is applied with a SetWindowPos that can activate the window, which is exactly what
    /// this card must never do.
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return parameters;
        }
    }

    private float DpiScale => DeviceDpi / 96f;

    private int L(float designPixels) => (int)Math.Round(designPixels * DpiScale);

    /// <summary>Device pixels per font pixel. Integer, so the glyphs stay on the grid.</summary>
    private int FontPixel => Math.Max(1, (int)Math.Round(DpiScale));

    private int SpritePixelSize => Math.Max(1, (int)Math.Round(SpritePixel * DpiScale));

    private bool _pinned;

    /// <summary>
    /// A pinned card stays up until unpinned, and is dragged rather than clicked: it does not count
    /// down, and grabbing it anywhere moves it. Still never takes focus.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Pinned
    {
        get => _pinned;
        set
        {
            _pinned = value;
            Cursor = value ? Cursors.SizeAll : Cursors.Hand;
        }
    }

    /// <summary>
    /// Where a pinned card goes when it is next shown, before being kept on screen. Null puts it
    /// in the corner like the waiting card. Once it is up, it stays where the user drags it.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point? PinnedLocation { get; set; }

    /// <summary>The small status card: dog, state, 5-hour figure and gauge only. Applied at the next show.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Mini { get; set; }

    /// <summary>Who to draw: their portrait on the waiting card, their tray sprites elsewhere.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Pet Pet { get; set; }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCAPTION = 2;

    /// <summary>
    /// Pinned, the whole card reports itself as a title bar, so Windows does the dragging —
    /// including snapping between monitors and the DPI change on the way. Unpinned it stays a
    /// plain client area, so the click that dismisses it still arrives.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        // Over a "title bar" Windows shows the arrow, not the form's Cursor, so the move cursor
        // has to be set here for the drag to look draggable.
        if (m.Msg == WM_SETCURSOR && _pinned && (m.LParam.ToInt64() & 0xFFFF) == HTCAPTION)
        {
            Cursor.Current = Cursors.SizeAll;
            m.Result = 1;
            return;
        }

        // A pinned card is all "title bar", so its double-click arrives as a non-client one.
        if (m.Msg == WM_NCLBUTTONDBLCLK && _pinned)
        {
            PinnedDoubleClicked?.Invoke(this, EventArgs.Empty);
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
        if (m.Msg == WM_NCHITTEST && _pinned)
        {
            // Everywhere but the close button, which must stay client area to receive its click.
            var lParam = m.LParam.ToInt64();
            var point = PointToClient(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
            if (!CloseBox.Contains(point))
            {
                m.Result = HTCAPTION;
            }
        }
    }

    private const int WM_SETCURSOR = 0x0020;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;

    /// <summary>Raised when a pinned card is double-clicked — a single click is the start of a drag.</summary>
    public event EventHandler? PinnedDoubleClicked;

    private bool _closeHot;

    /// <summary>Raised when the close button is clicked, after the card has hidden itself.</summary>
    public event EventHandler? CloseClicked;

    /// <summary>
    /// The close button's hit area: a small square in the screen's top-right corner, clear of the
    /// header text. On the mini card the 5-hour figure is drawn to leave room for it.
    /// </summary>
    private Rectangle CloseBox
    {
        get
        {
            var size = L(11);
            var inset = Mini ? L(Border + MiniFrame + 1) : L(Border + Frame + 3);
            return new Rectangle(ClientSize.Width - inset - size, inset, size, size);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SetCloseHot(CloseBox.Contains(e.Location));
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        SetCloseHot(false);
    }

    private void SetCloseHot(bool hot)
    {
        if (hot == _closeHot)
        {
            return;
        }

        _closeHot = hot;
        Cursor = hot ? Cursors.Hand : _pinned ? Cursors.SizeAll : Cursors.Hand;
        Invalidate(CloseBox);
    }

    /// <summary>
    /// A click on the close button only closes: it skips the Click event, which on the waiting
    /// card would also bring the chosen app to the front.
    /// </summary>
    protected override void OnClick(EventArgs e)
    {
        if (CloseBox.Contains(PointToClient(MousePosition)))
        {
            Dismiss();
            CloseClicked?.Invoke(this, EventArgs.Empty);
            return;
        }

        base.OnClick(e);
    }

    /// <summary>
    /// A 5×5 pixel-art cross, barely there until hovered: muted and mostly transparent at rest,
    /// the full title colour under the pointer.
    /// </summary>
    private void DrawCloseButton(Graphics graphics)
    {
        var box = CloseBox;
        var pixel = FontPixel;
        var x = box.X + ((box.Width - (5 * pixel)) / 2);
        var y = box.Y + ((box.Height - (5 * pixel)) / 2);

        using var brush = new SolidBrush(_closeHot ? TitleColor : Color.FromArgb(0x60, MutedColor));
        for (var i = 0; i < 5; i++)
        {
            graphics.FillRectangle(brush, x + (i * pixel), y + (i * pixel), pixel, pixel);
            graphics.FillRectangle(brush, x + ((4 - i) * pixel), y + (i * pixel), pixel, pixel);
        }
    }

    /// <summary>
    /// Moves a card fully inside a working area — the monitor it was saved on may have been
    /// unplugged, rearranged, or changed resolution since. Pure, so it can be tested.
    /// </summary>
    internal static Point KeepOnScreen(Rectangle card, Rectangle workingArea) => new(
        Math.Max(workingArea.Left, Math.Min(card.X, workingArea.Right - card.Width)),
        Math.Max(workingArea.Top, Math.Min(card.Y, workingArea.Bottom - card.Height)));

    /// <summary>Shows the waiting card, or refreshes it in place if it is already up.</summary>
    public void ShowPrompt(WaitingPrompt prompt) => ShowContent(CardContent.From(prompt));

    /// <summary>Shows the card with any content, or refreshes it in place if it is already up.</summary>
    public void ShowContent(CardContent content)
    {
        _content = content;
        _remaining = Lifetime;
        _lastTick = _shownAt = DateTime.UtcNow;

        // The handle must exist before DeviceDpi means anything, and before the layout can be
        // measured for the monitor the card will actually appear on.
        _ = Handle;
        Relayout();

        if (!Visible)
        {
            Show();
        }

        // Topmost is a band, not a guarantee: any topmost window raised later — the taskbar,
        // Task Manager, another always-on-top app — sits above this one, and the creation-time
        // style never climbs back. A pinned card is refreshed here on every state write and pose
        // tick, so re-asserting on each show keeps it on top. SWP_NOACTIVATE: still never takes focus.
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

        // The ticker drives the countdown and the bang's blink; a pinned card has neither, and
        // would otherwise repaint 20 times a second for as long as it is up.
        if (_pinned)
        {
            _ticker.Stop();
        }
        else
        {
            _ticker.Start();
        }

        Invalidate();
    }

    /// <summary>Hides the card. Safe to call when it is not showing.</summary>
    public void Dismiss()
    {
        _ticker.Stop();
        if (Visible)
        {
            Hide();
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
        Invalidate();
    }

    /// <summary>
    /// Sizes the card for the current DPI and puts it in the bottom-right of the working area,
    /// which is beside the tray for the usual bottom taskbar and still clear of it elsewhere.
    /// </summary>
    private void Relayout()
    {
        var size = Mini
            ? new Size(L(MiniWidth), L(MiniHeight))
            : new Size(
                L(ScreenWidth + (2 * (Border + Frame))),
                L(ScreenHeight + Border + Frame + Chin + Border));

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        var gap = L(ScreenMarginFromTaskbar);
        var location = new Point(workingArea.Right - size.Width - gap, workingArea.Bottom - size.Height - gap);

        if (_pinned && (Visible || PinnedLocation is not null))
        {
            // Already up: stay where it was dragged. Otherwise: where it was saved. Either way kept
            // inside the working area of the monitor nearest to it.
            var wanted = new Rectangle(Visible ? Location : PinnedLocation!.Value, size);
            location = KeepOnScreen(wanted, Screen.GetWorkingArea(wanted));
        }

        Bounds = new Rectangle(location, size);

        // Region does not take ownership of the one it replaces, and this runs on every show.
        var previousRegion = Region;
        Region = NotchedCorners(size, Math.Max(1, L(2)));
        previousRegion?.Dispose();

        _detailFont?.Dispose();
        _detailFont = CreateDetailFont(L(11));
    }

    /// <summary>
    /// Stepped corners rather than an antialiased radius: a window region is either in or out, so a
    /// smooth curve would come out jagged anyway, and a two-step notch reads as the pixel-art
    /// rounded corner the rest of the card already speaks.
    /// </summary>
    private static Region NotchedCorners(Size size, int step)
    {
        var region = new Region(new Rectangle(Point.Empty, size));
        foreach (var (x, y, flipX, flipY) in new[] { (0, 0, false, false), (size.Width, 0, true, false), (0, size.Height, false, true), (size.Width, size.Height, true, true) })
        {
            region.Exclude(Corner(x, y, 2 * step, step, flipX, flipY));
            region.Exclude(Corner(x, y, step, 2 * step, flipX, flipY));
        }

        return region;

        static Rectangle Corner(int x, int y, int width, int height, bool flipX, bool flipY) =>
            new(flipX ? x - width : x, flipY ? y - height : y, width, height);
    }

    /// <summary>
    /// The design's body face is a monospace; Cascadia Mono ships with Windows 11 and Consolas
    /// with everything since Vista. GDI+ quietly substitutes a sans for a missing family, so
    /// check the name that comes back rather than trusting the constructor.
    /// </summary>
    internal static Font CreateDetailFont(int pixelHeight)
    {
        foreach (var family in new[] { "Cascadia Mono", "Consolas" })
        {
            var font = new Font(family, pixelHeight, FontStyle.Regular, GraphicsUnit.Pixel);
            if (string.Equals(font.Name, family, StringComparison.OrdinalIgnoreCase))
            {
                return font;
            }

            font.Dispose();
        }

        return new Font(FontFamily.GenericMonospace, pixelHeight, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private void Tick()
    {
        var now = DateTime.UtcNow;

        // Paused while hovered: someone reading the card should not have it vanish mid-sentence.
        // A pinned card does not count down at all.
        if (!_pinned && !ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            _remaining -= now - _lastTick;
        }

        _lastTick = now;

        if (_remaining <= TimeSpan.Zero)
        {
            Dismiss();
            return;
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.None;
        graphics.PixelOffsetMode = PixelOffsetMode.None;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

        // Bezel: the border, then the frame inside it, then the screen.
        var border = L(Border);
        using (var brush = new SolidBrush(BorderColor))
        {
            graphics.FillRectangle(brush, ClientRectangle);
        }

        using (var brush = new SolidBrush(FrameColor))
        {
            graphics.FillRectangle(brush, border, border, ClientSize.Width - (2 * border), ClientSize.Height - (2 * border));
        }

        var screen = Mini
            ? new Rectangle(L(Border + MiniFrame), L(Border + MiniFrame), ClientSize.Width - (2 * L(Border + MiniFrame)), ClientSize.Height - (2 * L(Border + MiniFrame)))
            : new Rectangle(L(Border + Frame), L(Border + Frame), L(ScreenWidth), L(ScreenHeight));
        using (var brush = new SolidBrush(ScreenColor))
        {
            graphics.FillRectangle(brush, screen);
        }

        if (Mini)
        {
            PaintMini(graphics, screen);
            DrawCloseButton(graphics);
            return;
        }

        var fontPixel = FontPixel;

        // Header: where this came from on the left, the state on the right in its accent colour.
        PixelFont.Draw(graphics, "Claude Code", screen.X + L(Inset), screen.Y + L(HeaderY), fontPixel, 3, MutedColor);
        var stateWidth = PixelFont.Measure(_content.State, fontPixel, 3, bold: true);
        PixelFont.Draw(graphics, _content.State, screen.Right - L(Inset) - stateWidth, screen.Y + L(HeaderY), fontPixel, 3, _content.Accent, bold: true);

        DrawDog(graphics, screen.X + L(DogX), screen.Y + L(DogY));

        // Heading, wrapped to the text column; the design breaks "PERMISSION NEEDED" in two.
        var textX = screen.X + L(TextX);
        var textWidth = screen.Right - L(Inset) - textX;
        var y = screen.Y + L(TitleY);
        foreach (var line in PixelFont.Wrap(_content.Title.ToUpperInvariant(), textWidth, fontPixel, 3, bold: true))
        {
            PixelFont.Draw(graphics, line, textX, y, fontPixel, 3, TitleColor, bold: true);
            y += L(TitleLineHeight);
        }

        // Detail: the hook's own sentence, trimmed with an ellipsis rather than overrunning the bar.
        var detailTop = y - L(TitleLineHeight) + (PixelFont.GlyphHeight * fontPixel) + L(14);
        var detailBounds = new Rectangle(textX, detailTop, textWidth, screen.Y + L(BarY) - L(12) - detailTop);
        TextRenderer.DrawText(
            graphics,
            _content.Detail,
            _detailFont,
            detailBounds,
            MutedColor,
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        DrawBar(graphics, screen);
        DrawCloseButton(graphics);
    }

    /// <summary>
    /// The mini card: the tray dog, the state in its accent, the 5-hour figure, and the gauge.
    /// No detail text — at this size it would be unreadable, and the full card is a setting away.
    /// </summary>
    private void PaintMini(Graphics graphics, Rectangle screen)
    {
        var fontPixel = FontPixel;
        var dogPixel = Math.Max(1, (int)Math.Round(2 * DpiScale));
        var dogSize = DogSprites.Size * dogPixel;
        var dogX = screen.X + L(5);
        DrawTrayDog(graphics, _content.Dog ?? DogState.Waiting, dogX, screen.Y + ((screen.Height - dogSize) / 2), dogPixel);

        var textX = dogX + dogSize + L(8);
        var textY = screen.Y + L(8);
        PixelFont.Draw(graphics, _content.State, textX, textY, fontPixel, 1, _content.Accent, bold: true);

        var figure = _content.Gauge is { } used && _content.Hint is not null
            ? $"5H {used.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}%"
            : "5H -";
        var figureWidth = PixelFont.Measure(figure, fontPixel, 1, bold: true);
        PixelFont.Draw(graphics, figure, screen.Right - L(16) - figureWidth, textY, fontPixel, 1, TitleColor, bold: true);

        var bar = new Rectangle(textX, screen.Bottom - L(12), screen.Right - L(8) - textX, Math.Max(1, L(4)));
        using (var brush = new SolidBrush(BarTrackColor))
        {
            graphics.FillRectangle(brush, bar);
        }

        var fraction = Math.Clamp((_content.Gauge ?? 0) / 100, 0, 1);
        var filled = (int)Math.Round(bar.Width * fraction);
        if (filled > 0)
        {
            using var brush = new SolidBrush(TrayIconRenderer.QuotaColorFor(_content.Gauge ?? 0));
            graphics.FillRectangle(brush, bar.X, bar.Y, filled, bar.Height);
        }
    }

    /// <summary>Blits the portrait a design pixel at a time, as whole device-pixel squares.</summary>
    private void DrawDog(Graphics graphics, int originX, int originY)
    {
        if (_content.Dog is DogState.Sleeping && Pet == Pet.Cat)
        {
            // 22×14 at five design pixels is 110×70: centred on the tray sprite's 96×96 square.
            var cell = Math.Max(1, (int)Math.Round(5 * DpiScale));
            DrawGrid(graphics, DogSprites.CatSleepingPortrait, DogSprites.CatPalette, originX - L(7), originY + L(13), cell);
            return;
        }

        if (_content.Dog is { } pose)
        {
            DrawTrayDog(graphics, pose, originX, originY, Math.Max(1, (int)Math.Round(6 * DpiScale)));
            return;
        }

        if (DogSprites.Portrait(Pet) is var (portrait, portraitPalette))
        {
            // 18 wide at five design pixels is 90×100 for 20 rows, the dog portrait's footprint at
            // four. The duck's 23 rows are centred on that footprint rather than hung from its top.
            DrawGrid(graphics, portrait, portraitPalette, originX, originY - L((portrait.Length - 20) * 5 / 2f), Math.Max(1, (int)Math.Round(5 * DpiScale)));
            return;
        }

        var size = SpritePixelSize;

        // Driven off time since the card appeared rather than a frame counter, so the blink keeps
        // its rhythm whatever the tick rate.
        var bangVisible = (int)((DateTime.UtcNow - _shownAt) / BangBlink) % 2 == 0;

        var brushes = new SolidBrush?[DogSprites.Palette.Length];
        try
        {
            var sprite = DogSprites.WaitingPortrait;
            for (var row = 0; row < sprite.Length; row++)
            {
                for (var column = 0; column < sprite[row].Length; column++)
                {
                    var index = DogSprites.IndexOf(sprite[row][column]);
                    if (index == 0 || (index == DogSprites.BangIndex && !bangVisible))
                    {
                        continue;
                    }

                    brushes[index] ??= new SolidBrush(DogSprites.Palette[index]);
                    graphics.FillRectangle(brushes[index]!, originX + (column * size), originY + (row * size), size, size);
                }
            }
        }
        finally
        {
            foreach (var brush in brushes)
            {
                brush?.Dispose();
            }
        }
    }

    /// <summary>Blits any palette grid as solid squares of <paramref name="size"/> device pixels.</summary>
    private static void DrawGrid(Graphics graphics, string[] sprite, Color[] palette, int originX, int originY, int size)
    {
        for (var row = 0; row < sprite.Length; row++)
        {
            for (var column = 0; column < sprite[row].Length; column++)
            {
                var index = DogSprites.IndexOf(sprite[row][column]);
                if (index == 0)
                {
                    continue;
                }

                using var brush = new SolidBrush(palette[index]);
                graphics.FillRectangle(brush, originX + (column * size), originY + (row * size), size, size);
            }
        }
    }

    /// <summary>
    /// The 16px tray sprite for a pose, as <paramref name="size"/>-device-pixel squares. The full
    /// card uses six design pixels — 96px, the portrait's footprint at four — and the mini card two.
    /// There is no large portrait for the other poses, and the tray sprite scaled by a whole number
    /// is still the design's own pixels.
    /// </summary>
    private void DrawTrayDog(Graphics graphics, DogState pose, int originX, int originY, int size)
    {
        var (sprite, palette) = DogSprites.Tray(pose, Pet);
        DrawGrid(graphics, sprite, palette, originX, originY, size);
    }

    /// <summary>
    /// The countdown bar, draining right to left — or, on the status card, the session gauge in its
    /// quota colour — with the hint beside it.
    /// </summary>
    private void DrawBar(Graphics graphics, Rectangle screen)
    {
        var bar = new Rectangle(screen.X + L(Inset), screen.Y + L(BarY), L(BarWidth), Math.Max(1, L(BarHeight)));

        using (var brush = new SolidBrush(BarTrackColor))
        {
            graphics.FillRectangle(brush, bar);
        }

        var fraction = _content.Gauge is { } used
            ? Math.Clamp(used / 100, 0, 1)
            : Math.Clamp(_remaining / Lifetime, 0, 1);
        var filled = (int)Math.Round(bar.Width * fraction);
        if (filled > 0)
        {
            using var brush = new SolidBrush(_content.Gauge is { } gauge ? TrayIconRenderer.QuotaColorFor(gauge) : _content.Accent);
            graphics.FillRectangle(brush, bar.X, bar.Y, filled, bar.Height);
        }

        if (_content.Hint is { } hint)
        {
            var fontPixel = FontPixel;
            var width = PixelFont.Measure(hint, fontPixel, 1);
            var hintY = bar.Y + (bar.Height / 2) - (PixelFont.GlyphHeight * fontPixel / 2);
            PixelFont.Draw(graphics, hint, screen.Right - L(Inset) - width, hintY, fontPixel, 1, MutedColor);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ticker.Dispose();
            _detailFont?.Dispose();
        }

        base.Dispose(disposing);
    }
}
