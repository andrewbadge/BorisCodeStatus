using System.Drawing;
using System.Drawing.Drawing2D;

namespace BorisClaudeNotifications.Tray;

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

    private static readonly Color BorderColor = Color.FromArgb(0x3A, 0x36, 0x2B);
    private static readonly Color FrameColor = Color.FromArgb(0x0C, 0x0B, 0x08);
    private static readonly Color ScreenColor = Color.FromArgb(0x14, 0x12, 0x0C);
    private static readonly Color MutedColor = Color.FromArgb(0x9A, 0x93, 0x84);
    private static readonly Color TitleColor = DogSprites.Palette[2];
    private static readonly Color AccentColor = DogSprites.Palette[DogSprites.BangIndex];
    private static readonly Color BarTrackColor = Color.FromArgb(0x2A, 0x27, 0x1F);

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private readonly System.Windows.Forms.Timer _ticker = new() { Interval = 50 };
    private Font? _detailFont;
    private WaitingPrompt _prompt = WaitingPrompt.From(null);
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
        Text = "BorisClaudeNotifications — Claude is waiting";

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

    /// <summary>Shows the card, or refreshes it in place if it is already up.</summary>
    public void ShowPrompt(WaitingPrompt prompt)
    {
        _prompt = prompt;
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

        _ticker.Start();
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
        var size = new Size(
            L(ScreenWidth + (2 * (Border + Frame))),
            L(ScreenHeight + Border + Frame + Chin + Border));

        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(Point.Empty);
        var gap = L(ScreenMarginFromTaskbar);
        Bounds = new Rectangle(
            workingArea.Right - size.Width - gap,
            workingArea.Bottom - size.Height - gap,
            size.Width,
            size.Height);

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
    private static Font CreateDetailFont(int pixelHeight)
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
        if (!ClientRectangle.Contains(PointToClient(Cursor.Position)))
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

        var screen = new Rectangle(L(Border + Frame), L(Border + Frame), L(ScreenWidth), L(ScreenHeight));
        using (var brush = new SolidBrush(ScreenColor))
        {
            graphics.FillRectangle(brush, screen);
        }

        var fontPixel = FontPixel;

        // Header: where this came from on the left, the state on the right in its accent colour.
        PixelFont.Draw(graphics, "Claude Code", screen.X + L(Inset), screen.Y + L(HeaderY), fontPixel, 3, MutedColor);
        const string state = "Waiting";
        var stateWidth = PixelFont.Measure(state, fontPixel, 3, bold: true);
        PixelFont.Draw(graphics, state, screen.Right - L(Inset) - stateWidth, screen.Y + L(HeaderY), fontPixel, 3, AccentColor, bold: true);

        DrawDog(graphics, screen.X + L(DogX), screen.Y + L(DogY));

        // Heading, wrapped to the text column; the design breaks "PERMISSION NEEDED" in two.
        var textX = screen.X + L(TextX);
        var textWidth = screen.Right - L(Inset) - textX;
        var y = screen.Y + L(TitleY);
        foreach (var line in PixelFont.Wrap(_prompt.Title.ToUpperInvariant(), textWidth, fontPixel, 3, bold: true))
        {
            PixelFont.Draw(graphics, line, textX, y, fontPixel, 3, TitleColor, bold: true);
            y += L(TitleLineHeight);
        }

        // Detail: the hook's own sentence, trimmed with an ellipsis rather than overrunning the bar.
        var detailTop = y - L(TitleLineHeight) + (PixelFont.GlyphHeight * fontPixel) + L(14);
        var detailBounds = new Rectangle(textX, detailTop, textWidth, screen.Y + L(BarY) - L(12) - detailTop);
        TextRenderer.DrawText(
            graphics,
            _prompt.Detail,
            _detailFont,
            detailBounds,
            MutedColor,
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        DrawBar(graphics, screen);
    }

    /// <summary>Blits the portrait a design pixel at a time, as whole device-pixel squares.</summary>
    private void DrawDog(Graphics graphics, int originX, int originY)
    {
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

    /// <summary>The countdown bar, draining right to left, with the prompt's hint beside it.</summary>
    private void DrawBar(Graphics graphics, Rectangle screen)
    {
        var bar = new Rectangle(screen.X + L(Inset), screen.Y + L(BarY), L(BarWidth), Math.Max(1, L(BarHeight)));

        using (var brush = new SolidBrush(BarTrackColor))
        {
            graphics.FillRectangle(brush, bar);
        }

        var fraction = Math.Clamp(_remaining / Lifetime, 0, 1);
        var filled = (int)Math.Round(bar.Width * fraction);
        if (filled > 0)
        {
            using var brush = new SolidBrush(AccentColor);
            graphics.FillRectangle(brush, bar.X, bar.Y, filled, bar.Height);
        }

        if (_prompt.Hint is { } hint)
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
